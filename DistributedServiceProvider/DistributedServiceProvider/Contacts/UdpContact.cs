﻿//  Copyright 2010  Trust4 Developers
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
using System.Linq;
using System.Text;
using DistributedServiceProvider.Base;
using System.Net;
using System.IO;
using DistributedServiceProvider.MessageConsumers;
using System.Net.Sockets;
using System.Threading;
using ProtoBuf;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using DistributedServiceProvider.Contacts;
using DistributedServiceProvider;

namespace DistributedServiceProvider.Contacts
{
    public class UdpContact
        :Contact, IEquatable<UdpContact>
    {
        private byte[] addressBytes
        {
            get
            {
                return Extensible.GetValue<byte[]>(this, NEXT_PROTO_TAG);
            }
            set
            {
                Extensible.AppendValue(this, NEXT_PROTO_TAG, value);
            }
        }

        public IPAddress Ip
        {
            get
            {
                return new IPAddress(addressBytes);
            }
            private set
            {
                addressBytes = value.GetAddressBytes();
            }
        }

        public int Port
        {
            get
            {
                return Extensible.GetValue<int>(this, NEXT_PROTO_TAG + 1);
            }
            private set
            {
                Extensible.AppendValue<int>(this, NEXT_PROTO_TAG + 1, value);
            }
        }

        public UdpContact(Identifier512 id, Guid networkId, IPAddress ip, int port)
            :base(id, networkId)
        {
            Ip = ip;
            Port = port;
        }

        private UdpContact()
        {

        }

        public override TimeSpan Ping(Contact source, TimeSpan timeout)
        {
            UdpContact uSource = source as UdpContact;

            using (MemoryStream m = new MemoryStream())
            {
                using(BinaryWriter w = new BinaryWriter(m))
                {
                    w.Write((byte)PacketFlag.Ping);

                    WriteContact(w, uSource);

                    var callback = localTable.GetConsumer<Callback>(Callback.CONSUMER_ID);
                    var token = callback.AllocateToken();

                    w.Write(IPAddress.HostToNetworkOrder(token.Id));

                    try
                    {
                        SendUdpMessage(m.ToArray(), Ip, Port);
                        
                        DateTime start = DateTime.Now;
                        if (token.Wait((int)timeout.TotalMilliseconds))
                        {
                            Console.WriteLine("Reply from " + this);
                            return DateTime.Now - start;
                        }
                        Console.WriteLine("No reply from " + this);
                        return TimeSpan.MaxValue;
                    }
                    finally
                    {
                        callback.FreeToken(token);
                    }
                }
            }
        }

        private static void WriteContact(BinaryWriter w, UdpContact c)
        {
            byte[] idBytes = c.Identifier.GetBytes().ToArray();
            w.Write(IPAddress.HostToNetworkOrder(idBytes.Length));
            w.Write(idBytes);

            byte[] netIdBytes = c.NetworkId.ToByteArray();
            w.Write(IPAddress.HostToNetworkOrder(netIdBytes.Length));
            w.Write(netIdBytes);

            w.Write(IPAddress.HostToNetworkOrder(c.Port));

            byte[] addrBytes = c.Ip.GetAddressBytes();
            w.Write(IPAddress.HostToNetworkOrder(addrBytes.Length));
            w.Write(addrBytes);
        }

        private static UdpContact ReadContact(BinaryReader reader)
        {
            int idBytesLength = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            byte[] idBytes = reader.ReadBytes(idBytesLength);
            Identifier512 id = new Identifier512(idBytes);

            int netIdBytesLength = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            byte[] netIdBytes = reader.ReadBytes(netIdBytesLength);
            Guid netId = new Guid(netIdBytes);

            int port = IPAddress.NetworkToHostOrder(reader.ReadInt32());

            int addrBytesLength = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            byte[] addrBytes = reader.ReadBytes(addrBytesLength);
            IPAddress address = new IPAddress(addrBytes);

            var c = new UdpContact(id, netId, address, port);
            return c;
        }

        public override void Send(Contact source, Guid consumerId, byte[] message, bool reliable, bool ordered, int channel)
        {
            UdpContact uSource = source as UdpContact;

            using(MemoryStream m = new MemoryStream())
            {
                using(BinaryWriter w = new BinaryWriter(m))
                {
                    w.Write((byte)PacketFlag.Data);

                    WriteContact(w, uSource);

                    byte[] guidBytes = consumerId.ToByteArray();
                    w.Write(IPAddress.HostToNetworkOrder(guidBytes.Length));
                    w.Write(guidBytes);

                    w.Write(IPAddress.HostToNetworkOrder(message.Length));
                    w.Write(message);
                    
                    SendUdpMessage(m.ToArray(), Ip, Port);
                }
            }
        }

        private static DistributedRoutingTable localTable;
        private static bool listen = true;
        private static Thread listenThread;
        private static UdpClient client;
        public static void InitialiseUdp(DistributedRoutingTable localTable, int port)
        {
            UdpContact.localTable = localTable;

            client = new UdpClient(port);

            listenThread = new Thread(() =>
            {
                while (listen)
                {
                    var async = client.BeginReceive((a) => { }, null);

                    while (!async.IsCompleted && listen) { Thread.Sleep(10); }
                        Thread.Sleep(10);

                    IPEndPoint groupEP = default(IPEndPoint);
                    byte[] b = client.EndReceive(async, ref groupEP);

                    if (!listen)
                        break;

                    ThreadPool.QueueUserWorkItem((a) =>
                    {
                        using (MemoryStream m = new MemoryStream(b))
                        {
                            using (BinaryReader r = new BinaryReader(m))
                            {
                                PacketFlag f = (PacketFlag)r.ReadByte();

                                switch (f)
                                {
                                    case PacketFlag.Ping: ParsePing(r); break;
                                    case PacketFlag.Data: ParseData(r); break;
                                    case PacketFlag.WhoAreYou: ParseWhoAreYou(r); break;
                                    case PacketFlag.WhoAreYouReply: ParseWhoAreYouReply(r); break;
                                    default: Console.WriteLine("Unknown packet type " + f); break;
                                        break;
                                    case PacketFlag.WhoAreYou:
                                        ParseWhoAreYou(r);
                                        break;
                                    case PacketFlag.WhoAreYouReply:
                                        ParseWhoAreYouReply(r);
                                        break;
                                    default:
                                        Console.WriteLine("Unknown packet type " + f);
                                        break;
                                }
                            }
                        }
                    });
                }
            });
            listenThread.IsBackground = true;
            listenThread.Start();
        }

        private static void ParseWhoAreYouReply(BinaryReader r)
        {
            long uniqueNumber = IPAddress.HostToNetworkOrder(r.ReadInt64());

            int idLength = IPAddress.HostToNetworkOrder(r.ReadInt32());
            byte[] idBytes = r.ReadBytes(idLength);

            Identifier512 id = new Identifier512(idBytes);

            Action<Identifier512> reply;
            if (discoveryWaits.TryGetValue(uniqueNumber, out reply))
                reply(id);
        }

        static ConcurrentDictionary<long, Action<Identifier512>> discoveryWaits = new ConcurrentDictionary<long, Action<Identifier512>>();
        public static Identifier512 DiscoverIdentifier(IPAddress address, int port, TimeSpan timeout)
        {
            long uniqueNumber;
            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(m))
                {
                    w.Write((byte)PacketFlag.WhoAreYou);

                    byte[] addrBytes = address.GetAddressBytes();
                    w.Write(IPAddress.HostToNetworkOrder(addrBytes.Length));
                    w.Write(addrBytes);

                    w.Write(IPAddress.HostToNetworkOrder(port));

                    Random r = new Random();
                    List<byte> longBytes = new List<byte>();
                    longBytes.AddRange(BitConverter.GetBytes(r.Next()));
                    longBytes.AddRange(BitConverter.GetBytes(r.Next()));
                    uniqueNumber = BitConverter.ToInt64(longBytes.ToArray(), 0);
                    w.Write(IPAddress.HostToNetworkOrder(uniqueNumber));

                    SendUdpMessage(m.ToArray(), address, port);
                }
            }

            Identifier512 reply = null;
            var reset = new ManualResetEventSlim();
            Action<Identifier512> answer = (a) =>
                {
                    reply = a;
                    reset.Set();
                };

            discoveryWaits.AddOrUpdate(uniqueNumber, answer, (a, b) =>
            {
                throw new InvalidOperationException("Duplicate key in discovery");
            });

            reset.Wait(timeout);
            discoveryWaits.TryRemove(uniqueNumber, out answer);

            if (reply != null)
                return reply;
            else
                throw new TimeoutException("No reply");
        }

        private static void ParseWhoAreYou(BinaryReader r)
        {
            int addrLength = IPAddress.NetworkToHostOrder(r.ReadInt32());
            byte[] addrBytes = r.ReadBytes(addrLength);
            IPAddress replyTo = new IPAddress(addrBytes);

            int port = IPAddress.NetworkToHostOrder(r.ReadInt32());

            long uniqueNumber = IPAddress.NetworkToHostOrder(r.ReadInt64());

            using (MemoryStream m = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(m))
                {
                    w.Write((byte)PacketFlag.WhoAreYouReply);

                    w.Write(IPAddress.HostToNetworkOrder(uniqueNumber));

                    var b = localTable.LocalIdentifier.GetBytes().ToArray();
                    w.Write(IPAddress.HostToNetworkOrder(b.Length));
                    w.Write(b);
                }

                SendUdpMessage(m.ToArray(), replyTo, port);
            }
        }

        private static void ParsePing(BinaryReader reader)
        {
            UdpContact c = ReadContact(reader);

            if (c == null)
                return;

            long tokenId = IPAddress.NetworkToHostOrder(reader.ReadInt64());

            localTable.DeliverPing(c);

            var callback = localTable.GetConsumer<Callback>(Callback.CONSUMER_ID);
            callback.SendResponse(localTable.LocalContact, c, tokenId, new byte[] { 1, 3, 3, 7 });
                1,
                3,
                3,
                7
            });
        }

        private static void ParseData(BinaryReader reader)
        {
            UdpContact source = ReadContact(reader);

            if (source == null)
                return;

            int guidLength = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            byte[] guidBytes = reader.ReadBytes(guidLength);
            Guid consumerId = new Guid(guidBytes);

            int msgLength = IPAddress.NetworkToHostOrder(reader.ReadInt32());
            byte[] msg = reader.ReadBytes(msgLength);

            try
            {
                localTable.Deliver(source, consumerId, msg);
            }
            catch (Exception e)
            {
                Console.WriteLine("Delivering data caused exception " + e);
            }
        }

        public static void Stop()
        {
            listen = false;
            if (listenThread != null)
                listenThread.Join();
        }

        private static void SendUdpMessage(byte[] msg, IPAddress destination, int port)
        {
            lock (client)
            {
                client.Send(msg, msg.Length, new IPEndPoint(destination, port));
            }
        }

        private enum PacketFlag
            : byte
        {
            Ping = 0,
            Data = 1,
            WhoAreYou = 2,
            WhoAreYouReply = 3,
        }

        public override string ToString()
        {
            return "{ " + Ip + ":" + Port + " " + base.Identifier + "}";
        }

        public bool Equals(UdpContact other)
        {
            if (other.addressBytes == null && addressBytes != null || other.addressBytes != null && addressBytes == null)
                return false;
            else if (other.addressBytes != null && other.addressBytes.Length == addressBytes.Length)
            {
                for (int i = 0; i < addressBytes.Length; i++)
                {
                    if (addressBytes[i] != other.addressBytes[i])
                        return false;
                }
            }

            if (other.Port != Port)
                return false;

            if (other.NetworkId != NetworkId)
                return false;

            if (other.Identifier != Identifier)
                return false;

            return true;
        }

        public override int GetHashCode()
        {
            return Identifier.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            UdpContact c = obj as UdpContact;

            if (c == null)
                return false;

            return Equals(c);
        }
    }
}

