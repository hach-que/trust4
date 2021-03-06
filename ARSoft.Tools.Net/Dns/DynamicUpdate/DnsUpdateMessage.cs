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
	public class DnsUpdateMessage : DnsMessageBase
	{
		public DnsUpdateMessage()
		{
			OperationCode = OperationCode.Update;
		}

		private List<PrequisiteBase> _prequisites;
		private List<UpdateBase> _updates;

		/// <summary>
		/// Gets or sets the zone name
		/// </summary>
		public string ZoneName
		{
			get { return _questions.Count > 0 ? _questions[0].Name : null; }
			set { _questions = new List<DnsQuestion>() { new DnsQuestion(value, RecordType.Soa, RecordClass.Any) }; }
		}

		/// <summary>
		/// Gets or sets the entries in the prerequisites section
		/// </summary>
		public List<PrequisiteBase> Prequisites
		{
			get { return _prequisites ?? (_prequisites = new List<PrequisiteBase>()); }
			set { _prequisites = value; }
		}

		/// <summary>
		/// Gets or sets the entries in the update section
		/// </summary>
		public List<UpdateBase> Updates
		{
			get { return _updates ?? (_updates = new List<UpdateBase>()); }
			set { _updates = value; }
		}

		internal override bool IsForcingTcp
		{
			get { return false; }
		}

		protected override void PrepareEncoding()
		{
			_answerRecords = (Prequisites != null ? Prequisites.Cast<DnsRecordBase>().ToList() : new List<DnsRecordBase>());
			_authorityRecords = (Updates != null ? Updates.Cast<DnsRecordBase>().ToList() : new List<DnsRecordBase>());
		}

		protected override void FinishParsing()
		{
			Prequisites = _answerRecords.ConvertAll<PrequisiteBase>(record =>
			                                                        {
			                                                        	if ((record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
			                                                        	{
			                                                        		return new RecordExistsPrequisite(record.Name, record.RecordType);
			                                                        	}
			                                                        	else if (record.RecordClass == RecordClass.Any)
			                                                        	{
			                                                        		return new RecordExistsPrequisite(record);
			                                                        	}
			                                                        	else if ((record.RecordClass == RecordClass.None) && (record.RecordDataLength == 0))
			                                                        	{
			                                                        		return new RecordNotExistsPrequisite(record.Name, record.RecordType);
			                                                        	}
			                                                        	else if ((record.RecordClass == RecordClass.Any) && (record.RecordType == RecordType.Any))
			                                                        	{
			                                                        		return new NameIsInUsePrequisite(record.Name);
			                                                        	}
			                                                        	else if ((record.RecordClass == RecordClass.None) && (record.RecordType == RecordType.Any))
			                                                        	{
			                                                        		return new NameIsNotInUsePrequisite(record.Name);
			                                                        	}
			                                                        	else
			                                                        	{
			                                                        		return null;
			                                                        	}
			                                                        }).Where(prequisite => (prequisite != null)).ToList();

			Updates = _authorityRecords.ConvertAll<UpdateBase>(record =>
			                                                   {
			                                                   	if (record.TimeToLive != 0)
			                                                   	{
			                                                   		return new AddRecordUpdate(record);
			                                                   	}
			                                                   	else if ((record.RecordType == RecordType.Any) && (record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
			                                                   	{
			                                                   		return new DeleteAllRecordsUpdate(record.Name);
			                                                   	}
			                                                   	else if ((record.RecordClass == RecordClass.Any) && (record.RecordDataLength == 0))
			                                                   	{
			                                                   		return new DeleteRecordUpdate(record.Name, record.RecordType);
			                                                   	}
			                                                   	else if (record.RecordClass == RecordClass.Any)
			                                                   	{
			                                                   		return new DeleteRecordUpdate(record);
			                                                   	}
			                                                   	else
			                                                   	{
			                                                   		return null;
			                                                   	}
			                                                   }).Where(update => (update != null)).ToList();
		}
	}
}