// 
//  Copyright 2010  Trust4 Developers
// 
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
// 
//        http://www.apache.org/licenses/LICENSE-2.0
// 
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Data4
{
    public class Dht
    {
        private Contact p_Self = null;
        private List<Contact> p_Contacts = new List<Contact>();
        private IFormatter p_Formatter = null;
        private UdpClient m_UdpClient = null;
        private Thread m_UdpThread = null;
        private bool p_ShowDebug = false;
        private List<Entry> p_OwnedEntries = new List<Entry>();
        private ConcurrentBag<Entry> p_CachedEntries = new ConcurrentBag<Entry>();

        public event EventHandler<MessageEventArgs> OnReceived;

        public Dht(ID identifier, IPEndPoint endpoint)
        {
            this.p_Self = new Contact(identifier, endpoint);
            this.p_Formatter = new BinaryFormatter();

            // Start listening for events.
            IPEndPoint from = null;
            this.m_UdpClient = new UdpClient(endpoint.Port);
            this.m_UdpThread = new Thread(delegate()
            {
                try
                {
                    while (true)
                    {
                        byte[] result = this.m_UdpClient.Receive(ref from);
                        this.LogI(LogType.DEBUG, "Received a message from " + from.ToString());
                        this.OnReceive(from, result);
                    }
                }
                catch (Exception e)
                {
                    if (e is ThreadAbortException)
                        return;
                    Console.WriteLine(e.ToString());
                }
            }
            );
            //this.m_UdpThread.IsBackground = true;
            this.m_UdpThread.Start();
        }

        /// <summary>
        /// Adds a key-value entry to the DHT, storing the key-value pair on this node.
        /// </summary>
        public void Put(ID key, string value)
        {
            this.p_OwnedEntries.Add(new Entry(this.p_Self, key, value));
        }

        /// <summary>
        /// Remove a key-value pair from this node.
        /// </summary>
        public void Remove(ID key)
        {
            List<Entry> es = new List<Entry>();
            foreach (Entry e in this.p_OwnedEntries)
                if (e.Key == key)
                    es.Add(e);
            foreach (Entry e in es)
                this.p_OwnedEntries.Remove(e);
        }

        /// <summary>
        /// Retrieves a value from the DHT.
        /// </summary>
        public IList<Entry> Get(ID key)
        {
            ConcurrentBag<Entry> entries = new ConcurrentBag<Entry>();
            Dictionary<Thread, bool> done = new Dictionary<Thread, bool>();
            List<Thread> threads = new List<Thread>();

            // Add our own cached and owned values.
            foreach (Entry e in this.p_CachedEntries)
            {
                if (e.Key == key)
                    entries.Add(e);
            }
            foreach (Entry e in this.p_OwnedEntries)
            {
                if (e.Key == key)
                    entries.Add(e);
            }

            // Ask contacts
            foreach (Contact b in this.p_Contacts)
            {
                Thread t = null;
                Contact c = b;
                t = new Thread(delegate()
                {
                    try
                    {
                        FetchMessage fm = new FetchMessage(this, c, key);
                        fm.Send();
                        int ticks = 0;

                        // Wait until we receive data, or timeout.
                        while (!fm.Received && ticks < 1500)
                        {
                            Thread.Sleep(100);
                            ticks += 100;
                        }

                        if (fm.Received)
                        {
                            foreach (Entry e in fm.Values)
                            {
                                Console.WriteLine("Added " + e.Value + " to the entries.");
                                this.p_CachedEntries.Add(e);
                                entries.Add(e);
                            }

                            if (entries.Count == 0)
                                Console.WriteLine("There were no entries to add.");

                            done[t] = true;

                            Thread.MemoryBarrier();
                        }
                        else
                        {
                            Console.WriteLine("The node did not return in time.");
                            done[t] = true;

                            Thread.MemoryBarrier();

                            // Allow us to still retrieve requests and add them to the cache.
                            ticks = 0;
                            while (!fm.Received && ticks < 15000)
                            {
                                Thread.Sleep(100);
                                ticks += 100;
                            }

                            if (fm.Received)
                            {
                                foreach (Entry e in fm.Values)
                                {
                                    Console.WriteLine("Added " + e.Value + " to the cached entries.");
                                    this.p_CachedEntries.Add(e);
                                }
                            }
                            else
                                Console.WriteLine("The node still didn't reply after 15 seconds.  Is it online?");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                );
                threads.Add(t);
                done[t] = false;
                t.IsBackground = false;
                t.Start();
            }

            while (true)
            {
                bool stopped = true;
                foreach (Thread t in threads)
                {
                    if (done[t] != true)
                        stopped = false;
                }
                if (stopped)
                    break;
                Thread.Sleep(100);
            }

            return new List<Entry>(entries.ToArray());
        }

        /// <summary>
        /// Handles receiving data through the UdpClient.
        /// </summary>
        /// <param name="endpoint">The endpoint from which the message was received.</param>
        /// <param name="result">The data that was received.</param>
        private void OnReceive(IPEndPoint endpoint, byte[] result)
        {
            using (MemoryStream stream = new MemoryStream(result))
            {
                Message message = this.p_Formatter.Deserialize(stream) as Message;
                MessageEventArgs e = new MessageEventArgs(message);
                message.Dht = this;
                message.Sender = this.FindContactByEndPoint(endpoint);

                if (this.OnReceived != null)
                    this.OnReceived(this, e);

                // TODO: Is there a better way to do this?
                if (e.Message is FetchMessage)
                {
                    // Handle the fetch request.
                    FetchConfirmationMessage fcm = new FetchConfirmationMessage(this, message, this.OnFetch(e.Message as FetchMessage));
                    fcm.Send();

                    // TODO: Make sure that the confirmation message is received.
                }
                else if (e.SendConfirmation && !( e.Message is ConfirmationMessage ))
                {
                    ConfirmationMessage cm = new ConfirmationMessage(this, message, "");
                    cm.Send();

                    // TODO: Make sure that the confirmation message is received.  Probably should
                    //       implement confirmation of confirmations in ConformationMessage class itself.
                }
            }
        }

        /// <summary>
        /// Handle a fetch request.  This function should probably be moved elsewhere, but where?
        /// </summary>
        public List<Entry> OnFetch(FetchMessage request)
        {
            List<Entry> entries = new List<Entry>();
            foreach (Entry e in this.p_OwnedEntries)
                if (e.Key == request.Key)
                    entries.Add(e);
            foreach (Entry e in this.p_CachedEntries)
                if (e.Key == request.Key)
                    entries.Add(e);
            return entries;
        }

        public bool Debug
        {
            get { return this.p_ShowDebug; }
            set { this.p_ShowDebug = value; }
        }

        public void Close()
        {
            this.m_UdpThread.Abort();
            this.m_UdpClient.Close();
        }

        public IFormatter Formatter
        {
            get { return this.p_Formatter; }
        }

        public Contact Self
        {
            get { return this.p_Self; }
        }

        public List<Contact> Contacts
        {
            get { return this.p_Contacts; }
        }

        public enum LogType
        {
            ERROR,
            WARNING,
            INFO,
            DEBUG
        }

        public void LogI(LogType type, string msg)
        {
            string id = (this.p_Self.EndPoint != null) ? this.p_Self.EndPoint.ToString() + " :" : "";
            switch (type)
            {
                case LogType.ERROR:
                    Console.WriteLine("ERROR  : " + id + " " + msg);
                    break;
                case LogType.WARNING:
                    Console.WriteLine("WARNING: " + id + " " + msg);
                    break;
                case LogType.INFO:
                    Console.WriteLine("INFO   : " + id + " " + msg);
                    break;
                case LogType.DEBUG:
                    if (this.p_ShowDebug)
                        Console.WriteLine("DEBUG  : " + id + " " + msg);
                    break;
                default:
                    Console.WriteLine("UNKNOWN: " + id + " " + msg);
                    break;
            }
        }

        public static void LogS(LogType type, string msg)
        {
            switch (type)
            {
                case LogType.ERROR:
                    Console.WriteLine("ERROR  : " + msg);
                    break;
                case LogType.WARNING:
                    Console.WriteLine("WARNING: " + msg);
                    break;
                case LogType.INFO:
                    Console.WriteLine("INFO   : " + msg);
                    break;
                case LogType.DEBUG:
                    Console.WriteLine("DEBUG  : " + msg);
                    break;
                default:
                    Console.WriteLine("UNKNOWN: " + msg);
                    break;
            }
        }

        /// <summary>
        /// Returns the contact in the Contacts list that has the specified endpoint, or
        /// null if there was no contact.
        /// </summary>
        public Contact FindContactByEndPoint(IPEndPoint endpoint)
        {
            foreach (Contact c in this.p_Contacts)
            {
                if (c.EndPoint.Address.ToString() == endpoint.Address.ToString())
                    return c;
            }

            return null;
        }
    }
}

