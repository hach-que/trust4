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

namespace ARSoft.Tools.Net.Dns.DynamicUpdate
{
	public class RecordExistsPrequisite : PrequisiteBase
	{
		public DnsRecordBase Record { get; private set; }

		internal RecordExistsPrequisite() {}

		public RecordExistsPrequisite(string name, RecordType recordType)
			: base(name, recordType, RecordClass.Any, 0) {}

		public RecordExistsPrequisite(DnsRecordBase record)
			: base(record.Name, record.RecordType, record.RecordClass, 0)
		{
			Record = record;
		}

		internal override void ParseAnswer(byte[] resultData, int startPosition, int length) {}

		protected internal override int MaximumRecordDataLength
		{
			get { return (Record == null) ? 0 : Record.MaximumRecordDataLength; }
		}

		protected internal override void EncodeRecordData(byte[] messageData, int offset, ref int currentPosition, Dictionary<string, ushort> domainNames)
		{
			if (Record != null)
				Record.EncodeRecordData(messageData, offset, ref currentPosition, domainNames);
		}
	}
}