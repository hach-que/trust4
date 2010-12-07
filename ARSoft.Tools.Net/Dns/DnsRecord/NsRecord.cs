﻿#region Copyright and License
// Copyright 2010 Alexander Reinert
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARSoft.Tools.Net.Dns
{
	public class NsRecord : DnsRecordBase
	{
		public string NameServer { get; private set; }

		internal NsRecord() {}

		public NsRecord(string name, int timeToLive, string nameServer)
			: base(name, RecordType.Ns, RecordClass.INet, timeToLive)
		{
			NameServer = nameServer ?? String.Empty;
		}

		internal override void ParseAnswer(byte[] resultData, int startPosition, int length)
		{
			NameServer = DnsMessageBase.ParseDomainName(resultData, ref startPosition);
		}

		public override string ToString()
		{
			return base.ToString() + " " + NameServer;
		}

		protected internal override int MaximumRecordDataLength
		{
			get { return NameServer.Length + 2; }
		}

		protected internal override void EncodeRecordData(byte[] messageData, int offset, ref int currentPosition, Dictionary<string, ushort> domainNames)
		{
			DnsMessageBase.EncodeDomainName(messageData, offset, ref currentPosition, NameServer, true, domainNames);
		}
	}
}