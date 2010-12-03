﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DistributedServiceProvider.Base;
using DistributedServiceProvider.Contacts;

namespace Trust4.DataStorage
{
    public interface IDataStore
    {
        /// <summary>
        /// Puts a key-value pair into the local data store that's participating in the DHT.
        /// </summary>
        /// <param name="key">The unique key.</param>
        /// <param name="value">The value.</param>
        void Put(Identifier512 key, byte[] value);

        /// <summary>
        /// Deletes a key-value pair from the local data store that's participating in the DHT.
        /// </summary>
        /// <param name="key">The unique key.</param>
        void Delete(Identifier512 key);

        /// <summary>
        /// Returns a list of values retrieved from all of the connected peers for the selected
        /// key request.
        /// </summary>
        /// <param name="key">The unique key.</param>
        /// <param name="timeout">The timeout value for each peer.</param>
        IEnumerable<DataResult> Get(Identifier512 key);
    }
}
