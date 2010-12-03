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
	public class NsIdOption : EDnsOptionBase
	{
		public byte[] Payload { get; set; }

		public NsIdOption()
			: base(EDnsOptionType.NsId) {}

		internal override void ParseData(byte[] resultData, int startPosition, int length)
		{
			Payload = new byte[length];
			Buffer.BlockCopy(resultData, startPosition, Payload, 0, length);
		}

		internal override ushort DataLength
		{
			get { return (ushort) ((Payload == null) ? 0 : Payload.Length); }
		}

		internal override void EncodeData(byte[] messageData, ref int currentPosition)
		{
			if ((Payload != null) && (Payload.Length != 0))
			{
				Buffer.BlockCopy(Payload, 0, messageData, currentPosition, Payload.Length);
				currentPosition += Payload.Length;
			}
		}
	}
}