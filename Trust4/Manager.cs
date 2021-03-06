﻿//
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
using System.IO;
using System.Net;
using System.Threading;
using ARSoft.Tools.Net.Dns;
using System.Security.AccessControl;
using System.Net.Sockets;
using Data4;
using Admin4;

namespace Trust4
{
    public class Manager
    {
        private Settings p_Settings = null;
        private Mappings p_Mappings = null;

        private WebServer m_WebServer = null;
        private DnsServer m_DNSServer = null;
        private DnsProcess m_DNSProcess = null;
        private Dht p_Dht = null;

        private static readonly Guid m_P2PRootStore = new Guid("94e9bd40-2547-4232-9266-4f93310bf906");
        private static readonly Guid m_KeyRootStore = new Guid("09a2cbb4-ef12-431c-9419-5a655075039e");
        private static readonly Guid m_RootPseudonym = new Guid("3a88f92d-66d2-4d1f-87ee-ee90523ec47d");

        /// <summary>
        /// Creates a new Manager instance, which handles execution of the Trust4
        /// server.
        /// </summary>
        public Manager()
        {
            // Load the settings.
            this.p_Settings = new Settings("settings.txt");
            this.p_Settings.Load();
            this.p_Settings.Save();

            // Check to see if we require the application to be started as root.
            if (( this.p_Settings.AdminEnabled && this.p_Settings.AdminPort < 1024 ) ||
                ( this.p_Settings.DNSPort < 1024 ) ||
                ( this.p_Settings.P2PPort < 1024 ))
            {
                if (!UNIX.HasRootPermissions())
                {
                    Console.WriteLine("Error!  One of more of the ports specified in the settings.txt file is lower than 1024, therefore Trust4 must be started as root.");
                    return;
                }
            }
            
            // Initalize the web admin interface.
            if (!this.InitalizeAdmin())
                return;
            
            // Initalize the DNS service.
            if (!this.InitalizeDNS())
                return;

            // Couldn't lower permissions from root; exit immediately.
            // Initalize the DHT service.
            if (!this.InitalizeDHT())
                return;

            if (this.p_Settings.Configured)
            {
                // Load the mappings.
                this.InitalizeDomains();

                // Go online.
                this.p_Settings.Online = true;
                this.p_Settings.Initializing = false;
            }
            else if (this.p_Settings.AdminEnabled)
                Console.WriteLine("Your node is not yet configured.  You can configure it by accessing http://localhost:" + this.p_Settings.AdminPort + "/ while the server is running.");
            else
            {
                Console.WriteLine("Your node is not yet configured.  Before starting Trust4, you must configure it by editing the settings.txt file.");
                this.m_DNSServer.Stop();
                return;
            }

            // .. the Trust4 server is now running ..
            Thread.Sleep(Timeout.Infinite);
            //Console.WriteLine("Press any key to stop server.");
            //Console.ReadLine();
            
            // Stop the server
            this.m_DNSServer.Stop();
        }

        /// <summary>
        /// The settings for the Trust4 server.
        /// </summary>
        public Settings Settings
        {
            get { return this.p_Settings; }
        }

        /// <summary>
        /// The local and cached domain mappings.
        /// </summary>
        public Mappings Mappings
        {
            get { return this.p_Mappings; }
        }

        /// <summary>
        /// Returns the distributed routing table.
        /// </summary>
        public Dht Dht
        {
            get { return this.p_Dht; }
        }

        /// <summary>
        /// Whether the Trust4 server responds to incoming peer-to-peer requests as well
        /// as request records from peers.  The Trust4 server will still resolve cached
        /// domains when in offline mode.
        /// </summary>
        public bool Online
        {
            get { return this.p_Settings.Online; }
        }

        public bool InitalizeDomains()
        {
            // Load the mappings.
            this.p_Mappings = new Mappings(this, "mappings.txt");
            this.p_Mappings.Load();

            return true;
        }

        /// <summary>
        /// Initalizes the DNS server component.
        /// </summary>
        public bool InitalizeDNS()
        {
            if (this.p_Settings.Configured && this.m_DNSProcess == null && this.m_DNSServer == null)
            {
                // Create the DNS processing instance.
                this.m_DNSProcess = new DnsProcess(this);

                // Start the DNS server.
                this.m_DNSServer = new DnsServer(IPAddress.Any, this.p_Settings.DNSPort, 10, 10, this.m_DNSProcess.ProcessQuery);
                this.m_DNSServer.ExceptionThrown += new EventHandler<ExceptionEventArgs>(this.m_DNSProcess.ExceptionThrown);
                try
                {
                    this.m_DNSServer.Start();
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                        // Can't bind to port
                        Console.WriteLine("Error!  Can't bind to DNS port; it seems another program is currently using it.");
                    else if (ex.SocketErrorCode == SocketError.AccessDenied)
                        // Access denied
                        Console.WriteLine("Error!  Can't bind to DNS port; it seems that you don't have permissions to bind to the port.");
                    else
                        // Other
                        Console.WriteLine("Error!  Can't bind to DNS port; " + ex.Message);

                    return false;
                }
            }

            // Lower the permissions even if the DNS server didn't start (if configured).
            int p = (int) Environment.OSVersion.Platform;
            if (( p == 4 ) || ( p == 6 ) || ( p == 128 ))
            {
                if (this.p_Settings.Configured)
                {
                    if (!UNIX.UpdateUIDGID(this.Settings.UnixUID, this.Settings.UnixGID))
                    {
                        Console.WriteLine("Error!  I couldn't not lower the permissions of the current process.  I'm not going to continue for security reasons!");
                        return false;
                    }
                }
                else if (UNIX.HasRootPermissions())
                    Console.WriteLine("Warning!  The service will run as root until it is configured through the web interface!");
            }
            
            return true;
        }

        /// <summary>
        /// Initalizes the Distributed Hash Table component.
        /// </summary>
        public bool InitalizeDHT()
        {
            if (!this.p_Settings.Configured)
                return true;

            try
            {
                // Start the Distributed Hash Table.
                this.p_Dht = new Dht(this.p_Settings.RoutingIdentifier, new IPEndPoint(this.p_Settings.LocalIP, this.p_Settings.P2PPort));
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    // Can't bind to port
                    Dht.LogS(Dht.LogType.ERROR, "Unable to bind to the peer-to-peer to port.  Ensure you have permissions and that no other application is currently using.");
                else
                    Console.WriteLine(ex.Message);

                return false;
            }

            this.p_Dht.Debug = true;
            this.p_Dht.LogI(Dht.LogType.INFO, "Your routing identifier is " + this.p_Dht.Self.Identifier);
            this.p_Dht.LogI(Dht.LogType.INFO, "Adding contacts...");
            this.BootstrapPeers();
            this.p_Dht.LogI(Dht.LogType.INFO, "Contacts have been added.");

            return true;
        }

        /// <summary>
        /// Initalizes the administration interface.
        /// </summary>
        /// <returns></returns>
        private bool InitalizeAdmin()
        {
            if (this.p_Settings.AdminEnabled)
            {
                this.m_WebServer = new Admin4.WebServer(this.p_Dht);
                this.m_WebServer.Add(new Admin4.Pages.OverviewPage(this));
                this.m_WebServer.Add(new Admin4.Pages.ControlPage(this));
                this.m_WebServer.Add(new Admin4.Pages.PeersPage(this));
                this.m_WebServer.Add(new Admin4.Pages.DomainsPage(this));
                this.m_WebServer.Add(new Admin4.Pages.AutomaticConfigurationPage(this));
                HttpServer.HttpModules.FileModule s = new HttpServer.HttpModules.FileModule("/static/", "./static/");
                s.AddDefaultMimeTypes();
                this.m_WebServer.Add(s);
                this.m_WebServer.Start(IPAddress.Loopback, this.p_Settings.AdminPort);
            }

            // TODO: Catch SocketExceptions and other Exceptions from
            //       starting the web server and return false.

            return true;
        }

        public void ShutdownDHT()
        {
            if (this.p_Dht != null)
                this.p_Dht.Close();
            this.p_Dht = null;
            Dht.LogS(Dht.LogType.INFO, "The DHT node has now shutdown.");
        }

        /// <summary>
        /// Yeilds a list of peers as they are read from the peers.txt file.
        /// </summary>
        /// <returns>The next peer.</returns>
        public void BootstrapPeers()
        {
            // Only read peers.txt if it actually exists :)
            if (!File.Exists("peers.txt"))
                return;

            // Read all the peers.
            foreach (var line in File.ReadAllLines("peers.txt").OmitComments("#", "//"))
            {
                ID id = null;
                
                try
                {
                    string[] split = line.Split(new char[] {
                        ' ',
                        '\t'
                    }, StringSplitOptions.RemoveEmptyEntries);
                    
                    decimal trust = Decimal.Parse(split[0]);
                    
                    IPAddress ip = IPAddress.Parse(split[1]);
                    int port = Int32.Parse(split[2]);

                    if (split.Length == 7)
                    {
                        Guid a = new Guid(split[3]);
                        Guid b = new Guid(split[4]);
                        Guid c = new Guid(split[5]);
                        Guid d = new Guid(split[6]);
                        
                        id = new ID(a, b, c, d);
                    }
                    else
                    {
                        this.p_Dht.LogI(Dht.LogType.ERROR, "Autodiscovery of peer IDs is not yet supported.");
                        continue;
                    }

                    if (id != null)
                    {
                        this.p_Dht.Contacts.Add(new TrustedContact(trust, id, Guid.Empty, ip, port));
                        Console.WriteLine("Loaded bootstrap contact " + id);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception parsing bootstrap file: " + e);
                }
            }
        }

        /// <summary>
        /// Saves the peer information back to the original peers.txt file.
        /// </summary>
        public void SavePeers()
        {
            using (StreamWriter writer = new StreamWriter("peers.txt"))
            {
                writer.WriteLine("# The format of the peers.txt is:");
                writer.WriteLine("# TRUST   IP      PORT        ROUTING IDENTIFIER");
                foreach (Contact c in this.Dht.Contacts)
                {
                    decimal trust = 0;
                    if (c is TrustedContact)
                        trust = (c as TrustedContact).TrustAmount;
                    writer.WriteLine(trust + " " + c.EndPoint.Address + " " + c.EndPoint.Port + " " + c.Identifier);
                }
            }
        }
    }
}
