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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ARSoft.Tools.Net.Dns;
using Data4;

namespace Trust4
{
    public class Mappings
    {
        private string p_Path = null;
        private Manager m_Manager = null;
        private List<DomainMap> p_Domains = new List<DomainMap>();
        private List<string> m_WaitingOn = new List<string>();

        public Mappings(Manager manager, string path)
        {
            this.p_Path = path;
            this.m_Manager = manager;
        }

        public string Path
        {
            get { return this.p_Path; }
        }

        public ReadOnlyCollection<DomainMap> Domains
        {
            get { return this.p_Domains.AsReadOnly(); }
        }

        public static RSACryptoServiceProvider CreateRSA(string containerName)
        {
            CspParameters parms = new CspParameters();
            parms.KeyContainerName = containerName;
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(parms);
            return rsa;
        }

        public struct EncryptionGuids
        {
            public Guid Public;
            public Guid Private;

            public EncryptionGuids(string publicguid)
            {
                this.Public = new Guid(publicguid);
                this.Private = Guid.Empty;
            }

            public EncryptionGuids(string publicguid, string privateguid)
            {
                this.Public = new Guid(publicguid);
                this.Private = new Guid(privateguid);
            }
        }

        public struct EncryptorPair
        {
            private RSACryptoServiceProvider m_Crypto;
            public RSAParameters Public;
            public RSAParameters Private;

            public EncryptorPair(EncryptionGuids guids)
            {
                if (!Directory.Exists("keys"))
                    Directory.CreateDirectory("keys");

                // Create the crypto provider.
                this.m_Crypto = new RSACryptoServiceProvider();

                // Load or save the cryptographic data.
                if (File.Exists("keys/" + guids.Private + ".key"))
                    this.m_Crypto.FromXmlString(File.ReadAllText("keys/" + guids.Private + ".key"));
                else
                    File.WriteAllText("keys/" + guids.Private + ".key", this.m_Crypto.ToXmlString(true));

                // Set the public and private parameters.
                this.Public = this.m_Crypto.ExportParameters(false);
                this.Private = this.m_Crypto.ExportParameters(true);
            }

            public EncryptorPair(string publickey)
            {
                // Create the crypto provider.
                this.m_Crypto = new RSACryptoServiceProvider();

                // Load the public data.
                this.m_Crypto.FromXmlString(publickey);

                // Set the public and private parameters.  In this case, the private
                // parameters are the same as the public ones since we don't have
                // any private information.
                this.Public = this.m_Crypto.ExportParameters(false);
                this.Private = this.m_Crypto.ExportParameters(false);
            }

            public string ToXmlString(bool exportprivate)
            {
                return this.m_Crypto.ToXmlString(exportprivate);
            }
        }

        public static byte[] Sign(EncryptorPair pair, byte[] data)
        {
            // Final Format: SIGNATURE | RECORD DATA | PUBLIC KEY
            List<byte> tmp = new List<byte>();

            // First build the byte[] that is both the unencrypted data and the public key.
            tmp.Clear();
            foreach (byte b in data)
                tmp.Add(b);
            tmp.Add((byte) '|');
            foreach (byte b in ByteString.GetBytes(pair.ToXmlString(false)))
                tmp.Add(b);

            // Sign the data.
            byte[] publicdata = tmp.ToArray();
            byte[] signature = Mappings.SignBytes(publicdata, pair.Private);

            // Ensure that the signature is verifiable with the private and public information.
            if (!Mappings.VerifyBytes(publicdata, signature, pair.Private))
            {
                Console.WriteLine("CRYPTOGRAPHY ERROR: The generated signature could not be verified using the private key.");
                return new byte[] {};
            }
            if (!Mappings.VerifyBytes(publicdata, signature, pair.Public))
            {
                Console.WriteLine("CRYPTOGRAPHY ERROR: The generated signature could not be verified using the public key.");
                return new byte[] {};
            }

            // Combine it.
            tmp.Clear();
            foreach (byte b in ByteString.GetBytes(ByteString.GetBase32String(signature)))
                tmp.Add(b);
            tmp.Add((byte)'|');
            foreach (byte b in publicdata)
                tmp.Add(b);

            return tmp.ToArray();
        }

        public static byte[] Verify(byte[] publichash, byte[] data)
        {
            // Starting format: SIGNATURE | RECORD DATA | PUBLIC KEY
            List<byte> signature = new List<byte>();
            List<byte> recorddata = new List<byte>();
            List<byte> publickey = new List<byte>();
            List<byte> combined = new List<byte>();

            // Loop through the data and pull out the required parts.
            int mode = 0; // Start with Signature.
            foreach (byte b in data)
            {
                switch (mode)
                {
                    case 0:
                        if (b == (byte)'|')
                            mode += 1; // Switch to Record Data.
                        else
                            signature.Add(b);
                        break;
                    case 1:
                        if (b == (byte)'|')
                        {
                            mode += 1; // Switch to Public Key.
                            combined.Add((byte)'|');
                        }
                        else
                        {
                            recorddata.Add(b);
                            combined.Add(b);
                        }
                        break;
                    case 2:
                        if (b == (byte)'|')
                            mode += 1; // Switch off.
                        else
                        {
                            publickey.Add(b);
                            combined.Add(b);
                        }
                        break;
                }
            }

            // Undecode the signature from it's Base-32 state.
            signature = ByteString.GetBase32Bytes(ByteString.GetString(signature.ToArray())).ToList();

            // Verify that the public hash matches the hash of the public key.
            SHA256Managed sha = new SHA256Managed();
            if (!sha.ComputeHash(publickey.ToArray()).SequenceEqual(publichash))
            {
                Console.WriteLine("CRYPTOGRAPHY ERROR: The public key has been modified in this request.");
                return null;
            }
            Console.WriteLine("CRYPTOGRAPHY INFORMATION: The public key is valid for this request.");

            // Load the public key since we now know it's valid.
            EncryptorPair pair = new EncryptorPair(ByteString.GetString(publickey.ToArray()));

            // Verify that the signature is valid.
            if (!Mappings.VerifyBytes(combined.ToArray(), signature.ToArray(), pair.Public))
            {
                Console.WriteLine("CRYPTOGRAPHY ERROR: The signature for this request is invalid.");
            }

            // We're all good.
            return recorddata.ToArray();
        }

        public static byte[] SignBytes(byte[] data, RSAParameters key)
        {
            try
            {   
                // Create a new instance of RSACryptoServiceProvider using the 
                // key from RSAParameters.  
                RSACryptoServiceProvider alg = new RSACryptoServiceProvider();
    
                alg.ImportParameters(key);
    
                // Hash and sign the data. Pass a new instance of SHA1CryptoServiceProvider
                // to specify the use of SHA1 for hashing.
                return alg.SignData(data, new SHA1CryptoServiceProvider());
            }
            catch(CryptographicException e)
            {
                Console.WriteLine(e.Message);

                return null;
            }
        }
    
        public static bool VerifyBytes(byte[] data, byte[] signature, RSAParameters key)
        {
            try
            {
                // Create a new instance of RSACryptoServiceProvider using the 
                // key from RSAParameters.
                RSACryptoServiceProvider alg = new RSACryptoServiceProvider();
    
                alg.ImportParameters(key);
    
                // Verify the data using the signature.  Pass a new instance of SHA1CryptoServiceProvider
                // to specify the use of SHA1 for hashing.
                return alg.VerifyData(data, new SHA1CryptoServiceProvider(), signature);
    
            }
            catch(CryptographicException e)
            {
                Console.WriteLine(e.Message);
    
                return false;
            }
        }

        /// <summary>
        /// Adds the specified question-answer pair to the DHT, adding an intermediatary CNAME record
        /// to prevent modification of the end destination for the domain.
        /// </summary>
        /// <param name="question">The original DNS question that will be asked.</param>
        /// <param name="answer">The original DNS answer that should be returned.</param>
        /// <param name="guids">The encryption GUIDs for this domain record.</param>
        public void Add(DnsQuestion question, DnsRecordBase answer, EncryptionGuids guids)
        {
            this.Add(question, answer, guids, false);
        }

        /// <summary>
        /// Adds the specified question-answer pair to the DHT, adding an intermediatary CNAME record
        /// to prevent modification of the end destination for the domain.
        /// </summary>
        /// <param name="question">The original DNS question that will be asked.</param>
        /// <param name="answer">The original DNS answer that should be returned.</param>
        /// <param name="guids">The encryption GUIDs for this domain record.</param>
        /// <param name="reverse">
        /// Whether the first (original) question should result in the original type of record, rather than
        /// a CNAME.  In this case, the second (.key domain) is a CNAME to the target listed in the answer.
        /// Used for when the question does not support having CNAME records returned (e.g. NS records).
        /// </param>
        public void Add(DnsQuestion question, DnsRecordBase answer, EncryptionGuids guids, bool reverse)
        {
            // Calculate the public key hash.
            EncryptorPair pair = new EncryptorPair(guids);
            SHA256Managed sha = new SHA256Managed();
            string publichash = ByteString.GetBase32String(sha.ComputeHash(ByteString.GetBytes(pair.ToXmlString(false))));

            // First automatically create a DnsRecordBase based on the question.
            string keydomain = null;
            DnsRecordBase keyanswer = null;
            if (!reverse)
            {
                keydomain = question.Name.ToLowerInvariant() + "." + publichash + ".key";
                keyanswer = new CNameRecord(question.Name.ToLowerInvariant(), 3600, keydomain);
            }
            else
            {
                DnsRecordBase newanswer = null;
                if (answer is NsRecord)
                {
                    keydomain = ( answer as NsRecord ).NameServer + "." + publichash + "." + question.Name.ToLowerInvariant() + "." + publichash + ".key";
                    newanswer = new CNameRecord(keydomain, 3600, ( answer as NsRecord ).NameServer);
                    answer = new NsRecord(question.Name.ToLowerInvariant(), 3600, keydomain);
                }
                else
                    throw new ArgumentException("reverse was true, but the specified record type could not be recreated with the new target.");
                keyanswer = answer;
                answer = newanswer;
            }

            // Add that CNAME record to the DHT.
            ID questionid = ID.NewHash(DnsSerializer.ToStore(question));
            this.m_Manager.Dht.Put(questionid, DnsSerializer.ToStore(keyanswer));
            
            // Now create a CNAME question that will be asked after looking up the original domain.
            DnsQuestion keyquestion = new DnsQuestion(keydomain, RecordType.CName, RecordClass.INet);
            
            // Add the original answer to the DHT, but encrypt it using our private key.
            ID keyquestionid = ID.NewHash(DnsSerializer.ToStore(keyquestion));
            this.m_Manager.Dht.Put(
                keyquestionid,
                ByteString.GetString(
                    Mappings.Sign(
                        new EncryptorPair(guids),
                        ByteString.GetBytes(
                            DnsSerializer.ToStore(
                                answer
                                )
                            )
                        )
                    )
                );
            
            // Add the domain to our cache.
            this.p_Domains.Add(new DomainMap(question, keyanswer));
            this.p_Domains.Add(new DomainMap(keyquestion, answer));
        }

        /// <summary>
        /// Adds the specified question-answer pair to the DHT, without any public-private key pair
        /// translation.  It is expected that this record points to a .key domain in it's answer.
        /// </summary>
        /// <param name="question">The original DNS question that will be asked.</param>
        /// <param name="answer">The original DNS answer that should be returned.</param>
        public void Add(DnsQuestion question, DnsRecordBase answer)
        {
            // Add the record to the DHT.
            ID questionid = ID.NewHash(DnsSerializer.ToStore(question));
            this.m_Manager.Dht.Put(questionid, DnsSerializer.ToStore(answer));
            
            // Add the domain to our cache.
            this.p_Domains.Add(new DomainMap(question, answer));
        }

        /// <summary>
        /// Pushes the answer for the specified question if we know the answer.  Returns
        /// true if the answer was pushed onto the return message.
        /// </summary>
        /// <param name="msg">A reference to the return message to which answer records should be added.</param>
        /// <param name="question">The original DNS question.</param>
        /// <returns>Whether the answer was added to the return message.</returns>
        public bool Fetch(ref DnsMessage msg, DnsQuestion question)
        {
            bool found = false;
            foreach (DomainMap m in this.p_Domains)
            {
                Console.Write(m.Domain + " == " + question.Name + "? ");
                if (m.Domain == question.Name)
                {
                    Console.WriteLine("yes");
                    msg.AnswerRecords.Add(m.Answer);
                    found = true;
                }

                else
                    Console.WriteLine("no");
            }
            
            return found;
        }

        /// <summary>
        /// Loads the domain records from the mappings.txt file. 
        /// </summary>
        public void Load()
        {
            // Check to ensure the path exists; if it doesn't, automatically create it.
            if (!File.Exists(this.p_Path))
            {
                using (StreamWriter writer = new StreamWriter(this.p_Path))
                {
                    writer.Write(Mappings.m_DefaultMappings);
                }
            }

            // Clear any existing data.
            foreach (DomainMap dm in this.p_Domains)
            {
                ID questionid = ID.NewHash(DnsSerializer.ToStore(dm.Question));
                this.m_Manager.Dht.Remove(questionid);
            }
            this.p_Domains.Clear();

            // Load the data.
            using (StreamReader reader = new StreamReader(this.p_Path))
            {
                foreach (var s in File.ReadAllLines(this.p_Path).OmitComments("#", "//").Select(a => a.ToLowerInvariant().Split(new char[] {
                    '\t',
                    ' '
                }, StringSplitOptions.RemoveEmptyEntries)))
                {
                    string type = s[0].Trim();
                    
                    try
                    {
                        string domain = null;
                        string target = null;
                        string publicguid = null;
                        string privateguid = null;
                        string priority = null;
                        string tdomain = null;
                        switch (type.ToUpperInvariant())
                        {
                            case "A":
                                domain = s[1].Trim();
                                target = s[2].Trim();
                                publicguid = s[3].Trim();
                                privateguid = s[4].Trim();
                                Console.Write("Mapping (" + type.ToUpperInvariant() + ") " + domain + " to " + target + "... ");
                                IPAddress o = IPAddress.None;
                                IPAddress.TryParse(target, out o);
                                this.Add(new DnsQuestion(domain, RecordType.A, RecordClass.INet), new ARecord(domain, 3600, o), new EncryptionGuids(publicguid, privateguid));
                                Console.WriteLine("done.");
                                break;
                            case "CNAME":
                                domain = s[1].Trim();
                                target = s[2].Trim();
                                publicguid = s[3].Trim();
                                privateguid = s[4].Trim();
                                Console.Write("Mapping (" + type.ToUpperInvariant() + ") " + domain + " to " + target + "... ");
                                this.Add(new DnsQuestion(domain, RecordType.A, RecordClass.INet), new CNameRecord(domain, 3600, target), new EncryptionGuids(publicguid, privateguid));
                                Console.WriteLine("done.");
                                break;
                            case "MX":
                                priority = s[1].Trim();
                                domain = s[2].Trim();
                                target = s[3].Trim();
                                Console.Write("Mapping (" + type.ToUpperInvariant() + ") " + domain + " to " + target + " with priority " + priority + "... ");
                                
                                // Get the target domain.
                                tdomain = this.GetPublicCNAME(target);
                                if (tdomain == null)
                                {
                                    Console.WriteLine("failed.");
                                    Console.WriteLine("A record must exist for domain target when MX record reached.  " + "Place the A record earlier in your mappings file or add it if needed.  " + "The MX record will be ignored.");
                                    break;
                                }

                                
                                this.Add(new DnsQuestion(domain, RecordType.A, RecordClass.INet), new MxRecord(domain, 3600, Convert.ToUInt16(priority), tdomain));
                                Console.WriteLine("done.");
                                break;
                            case "NS":
                                domain = s[1].Trim();
                                target = s[2].Trim();
                                publicguid = s[3].Trim();
                                privateguid = s[4].Trim();
                                Console.Write("Mapping (" + type.ToUpperInvariant() + ") " + domain + " to " + target + "... ");
                                this.Add(new DnsQuestion(domain, RecordType.Ns, RecordClass.INet), new NsRecord(domain, 3600, target), new EncryptionGuids(publicguid, privateguid), true);
                                Console.WriteLine("done.");
                                break;
                            default:
                                Console.WriteLine("failed.");
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("failed.");
                        Console.WriteLine(e.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Gets the translated name for domain.p2p (such as domain.p2p.publickey.key). 
        /// </summary>
        /// <param name="domain">
        /// A <see cref="System.String"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        public string GetPublicCNAME(string domain)
        {
            foreach (DomainMap d in this.p_Domains)
                if (d.Domain == domain)
                {
                    return d.CNAMETarget;
                }
            
            return null;
        }

        /// <summary>
        /// Adds the domain to the WaitingOn list.
        /// </summary>
        /// <param name="domain">The domain name.</param>
        public void BeginWait(string domain)
        {
            this.m_WaitingOn.Add(domain);
        }

        /// <summary>
        /// Removes the domain from the WaitingOn list.
        /// </summary>
        /// <param name="domain">The domain name.</param>
        public void EndWait(string domain)
        {
            this.m_WaitingOn.Remove(domain);
        }

        /// <summary>
        /// Returns whether we're waiting on a resolution for this domain name.
        /// </summary>
        /// <param name="domain">The domain name.</param>
        public bool Waiting(string domain)
        {
            return this.m_WaitingOn.Contains(domain);
        }

        private const string m_DefaultMappings = @"# An example of mapping a domain and it's subdomains to a specified IP address.  For A records,
# the format is:
#
# TYPE        DOMAIN               TARGET                   PUBLIC GUID                               PRIVATE GUID
# A           opensuse.p2p         130.57.5.70              e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# A           mx.opensuse.p2p      64.223.4.1               e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# A           mx2.opensuse.p2p     121.34.91.43             e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# A           mx3.opensuse.p2p     130.57.5.70              e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287

# An example of mapping MX records to subdomains of.  For MX records, the format is:
#
# TYPE        PRIORITY              DOMAIN                  TARGET
# MX          10                    opensuse.p2p            mx.opensuse.p2p
# MX          20                    opensuse.p2p            mx2.opensuse.p2p
# MX          30                    opensuse.p2p            mx3.opensuse.p2p

# An example of delegating a .p2p domain to a standard non-Trust4 nameserver.  This means that
# the specified nameserver will handle all of the records (useful for pointing a domain to a webserver
# and then managing the subdomains on the server itself rather than through P2P). The
# format of NS records are:
#
# TYPE        DOMAIN                NAMESERVER              PUBLIC GUID                               PRIVATE GUID
# NS          redpoint.p2p          ns1.linode.com          e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# NS          redpoint.p2p          ns2.linode.com          e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# NS          redpoint.p2p          ns3.linode.com          e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# NS          redpoint.p2p          ns4.linode.com          e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287
# NS          redpoint.p2p          ns5.linode.com          e96c5ebc-5f36-46e4-8ea7-a30100e6f8aa      972ce690-695c-435f-99c2-98cf47d79287";
    }
}
