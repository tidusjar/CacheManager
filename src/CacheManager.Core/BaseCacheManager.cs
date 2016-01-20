﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CacheManager.Core.Internal;
using static CacheManager.Core.Utility.Guard;

namespace CacheManager.Core
{
    /// <summary>
    /// The BaseCacheManager implements <see cref="ICacheManager{T}"/> and is the main class which
    /// gets constructed by <see cref="CacheFactory"/>.
    /// <para>
    /// The cache manager manages the list of <see cref="BaseCacheHandle{T}"/>'s which have been
    /// added. It will keep them in sync depending on the configuration.
    /// </para>
    /// </summary>
    /// <typeparam name="TCacheValue">The type of the cache value.</typeparam>
    public sealed class BaseCacheManager<TCacheValue> : BaseCache<TCacheValue>, ICacheManager<TCacheValue>, IDisposable
    {
        /// <summary>
        /// The cache back plate.
        /// </summary>
        private CacheBackPlate cacheBackPlate;

        /// <summary>
        /// The cache handles collection.
        /// </summary>
        private BaseCacheHandle<TCacheValue>[] cacheHandles;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseCacheManager{TCacheValue}"/> class
        /// using the specified configuration.
        /// </summary>
        /// <param name="name">The cache name.</param>
        /// <param name="configuration">
        /// The configuration which defines the name of the manager and contains information of the
        /// cache handles this instance should manage.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// When <paramref name="configuration"/> is null.
        /// </exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "nope")]
        public BaseCacheManager(string name, CacheManagerConfiguration configuration)
        {
            NotNullOrWhiteSpace(name, nameof(name));
            NotNull(configuration, nameof(configuration));

            this.Name = name;
            this.Configuration = configuration;
            this.cacheHandles = CacheReflectionHelper.CreateCacheHandles(this).ToArray();

            if (this.Configuration.HasBackPlate)
            {
                this.RegisterCacheBackPlate(CacheReflectionHelper.CreateBackPlate(this));
            }
        }

        /// <summary>
        /// Occurs when an item was successfully added to the cache.
        /// <para>The event will not get triggered if <c>Add</c> would return false.</para>
        /// </summary>
        public event EventHandler<CacheActionEventArgs> OnAdd;

        /// <summary>
        /// Occurs when <c>Clear</c> gets called, after the cache has been cleared.
        /// </summary>
        public event EventHandler<CacheClearEventArgs> OnClear;

        /// <summary>
        /// Occurs when <c>ClearRegion</c> gets called, after the cache region has been cleared.
        /// </summary>
        public event EventHandler<CacheClearRegionEventArgs> OnClearRegion;

        /// <summary>
        /// Occurs when an item was retrieved from the cache.
        /// <para>The event will only get triggered on cache hit. Misses do not trigger!</para>
        /// </summary>
        public event EventHandler<CacheActionEventArgs> OnGet;

        /// <summary>
        /// Occurs when an item was put into the cache.
        /// </summary>
        public event EventHandler<CacheActionEventArgs> OnPut;

        /// <summary>
        /// Occurs when an item was successfully removed from the cache.
        /// </summary>
        public event EventHandler<CacheActionEventArgs> OnRemove;

        /// <summary>
        /// Occurs when an item was successfully updated.
        /// </summary>
        public event EventHandler<CacheUpdateEventArgs<TCacheValue>> OnUpdate;

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public CacheManagerConfiguration Configuration { get; }

        /// <summary>
        /// Gets a list of cache handles currently registered within the cache manager.
        /// </summary>
        /// <value>The cache handles.</value>
        /// <remarks>
        /// This list is read only, any changes to the returned list instance will not affect the
        /// state of the cache manager instance.
        /// </remarks>
        public IEnumerable<BaseCacheHandle<TCacheValue>> CacheHandles
            => new ReadOnlyCollection<BaseCacheHandle<TCacheValue>>(
                new List<BaseCacheHandle<TCacheValue>>(
                    this.cacheHandles));

        /// <summary>
        /// Gets the cache name.
        /// </summary>
        /// <value>The name of the cache.</value>
        public string Name { get; }

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="addValue">
        /// The value which should be added in case the item doesn't already exist.
        /// </param>
        /// <param name="updateValue">
        /// The function to perform the update in case the item does already exist.
        /// </param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public TCacheValue AddOrUpdate(string key, TCacheValue addValue, Func<TCacheValue, TCacheValue> updateValue) =>
            this.AddOrUpdate(key, addValue, updateValue, new UpdateItemConfig());

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="addValue">
        /// The value which should be added in case the item doesn't already exist.
        /// </param>
        /// <param name="updateValue">
        /// The function to perform the update in case the item does already exist.
        /// </param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public TCacheValue AddOrUpdate(string key, string region, TCacheValue addValue, Func<TCacheValue, TCacheValue> updateValue) =>
            this.AddOrUpdate(key, region, addValue, updateValue, new UpdateItemConfig());

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="addValue">
        /// The value which should be added in case the item doesn't already exist.
        /// </param>
        /// <param name="updateValue">
        /// The function to perform the update in case the item does already exist.
        /// </param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> or <paramref name="config"/>
        /// are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public TCacheValue AddOrUpdate(string key, TCacheValue addValue, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config) =>
            this.AddOrUpdate(new CacheItem<TCacheValue>(key, addValue), updateValue, config);

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="addValue">
        /// The value which should be added in case the item doesn't already exist.
        /// </param>
        /// <param name="updateValue">
        /// The function to perform the update in case the item does already exist.
        /// </param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// or <paramref name="config"/> are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public TCacheValue AddOrUpdate(string key, string region, TCacheValue addValue, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config) =>
            this.AddOrUpdate(new CacheItem<TCacheValue>(key, region, addValue), updateValue, config);

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="addItem">The item which should be added or updated.</param>
        /// <param name="updateValue">The function to perform the update, if the item does exist.</param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="addItem"/> or <paramref name="updateValue"/> are null.
        /// </exception>
        public TCacheValue AddOrUpdate(CacheItem<TCacheValue> addItem, Func<TCacheValue, TCacheValue> updateValue) =>
            this.AddOrUpdate(addItem, updateValue, new UpdateItemConfig());

        /// <summary>
        /// Adds an item to the cache or, if the item already exists, updates the item using the
        /// <paramref name="updateValue"/> function.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="addItem">The item which should be added or updated.</param>
        /// <param name="updateValue">The function to perform the update, if the item does exist.</param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <returns>
        /// The value which has been added or updated, or null, if the update was not successful.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="addItem"/> or <paramref name="updateValue"/> or
        /// <paramref name="config"/> are null.
        /// </exception>
        public TCacheValue AddOrUpdate(CacheItem<TCacheValue> addItem, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config)
        {
            NotNull(addItem, nameof(addItem));
            NotNull(updateValue, nameof(updateValue));
            NotNull(config, nameof(config));

            return this.AddOrUpdateInternal(addItem, updateValue, config);
        }

        /// <summary>
        /// Clears this cache, removing all items in the base cache and all regions.
        /// </summary>
        public override void Clear()
        {
            foreach (var handle in this.cacheHandles)
            {
                handle.Clear();
                handle.Stats.OnClear();
            }

            if (this.Configuration.HasBackPlate)
            {
                this.cacheBackPlate.NotifyClear();
            }

            this.TriggerOnClear();
        }

        /// <summary>
        /// Clears the cache region, removing all items from the specified <paramref name="region"/> only.
        /// </summary>
        /// <param name="region">The cache region.</param>
        /// <exception cref="System.ArgumentNullException">If region is null.</exception>
        public override void ClearRegion(string region)
        {
            NotNullOrWhiteSpace(region, nameof(region));

            foreach (var handle in this.cacheHandles)
            {
                handle.ClearRegion(region);
                handle.Stats.OnClearRegion(region);
            }

            if (this.Configuration.HasBackPlate)
            {
                this.cacheBackPlate.NotifyClearRegion(region);
            }

            this.TriggerOnClearRegion(region);
        }

        /// <summary>
        /// Changes the expiration <paramref name="mode" /> and <paramref name="timeout" /> for the
        /// given <paramref name="key" />.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="mode">The expiration mode.</param>
        /// <param name="timeout">The expiration timeout.</param>
        public override void Expire(string key, ExpirationMode mode, TimeSpan timeout)
        {
            foreach (var handle in this.cacheHandles)
            {
                handle.Expire(key, mode, timeout);
            }
        }

        /// <summary>
        /// Changes the expiration <paramref name="mode" /> and <paramref name="timeout" /> for the
        /// given <paramref name="key" />.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="region">The cache region.</param>
        /// <param name="mode">The expiration mode.</param>
        /// <param name="timeout">The expiration timeout.</param>
        public override void Expire(string key, string region, ExpirationMode mode, TimeSpan timeout)
        {
            foreach (var handle in this.cacheHandles)
            {
                handle.Expire(key, region, mode, timeout);
            }
        }

        /// <summary>
        /// Returns a <see cref="string" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="string" /> that represents this instance.
        /// </returns>
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} Handles: {1}", this.Name, this.cacheHandles.Length);

        /// <summary>
        /// Tries to update an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="value">The updated value, or null, if the update was not successful.</param>
        /// <returns><c>True</c> if the update operation was successful, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public bool TryUpdate(string key, Func<TCacheValue, TCacheValue> updateValue, out TCacheValue value) =>
            this.TryUpdate(key, updateValue, new UpdateItemConfig(), out value);

        /// <summary>
        /// Tries to update an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="value">The updated value, or null, if the update was not successful.</param>
        /// <returns><c>True</c> if the update operation was successful, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public bool TryUpdate(string key, string region, Func<TCacheValue, TCacheValue> updateValue, out TCacheValue value) =>
            this.TryUpdate(key, region, updateValue, new UpdateItemConfig(), out value);

        /// <summary>
        /// Tries to update an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <param name="value">The updated value, or null, if the update was not successful.</param>
        /// <returns><c>True</c> if the update operation was successful, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> or <paramref name="config"/>
        /// are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public bool TryUpdate(string key, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config, out TCacheValue value)
        {
            NotNullOrWhiteSpace(key, nameof(key));
            NotNull(updateValue, nameof(updateValue));
            NotNull(config, nameof(config));

            return this.UpdateInternal(this.cacheHandles, key, updateValue, config, out value);
        }

        /// <summary>
        /// Tries to update an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <param name="value">The updated value, or null, if the update was not successful.</param>
        /// <returns><c>True</c> if the update operation was successful, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// or <paramref name="config"/> are null.
        /// </exception>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        public bool TryUpdate(string key, string region, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config, out TCacheValue value)
        {
            NotNullOrWhiteSpace(key, nameof(key));
            NotNullOrWhiteSpace(region, nameof(region));
            NotNull(updateValue, nameof(updateValue));
            NotNull(config, nameof(config));

            return this.UpdateInternal(this.cacheHandles, key, region, updateValue, config, out value);
        }

        /// <summary>
        /// Updates an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        /// <param name="key">The key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <returns><c>True</c> if the update operation was successfully, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> is null.
        /// </exception>
        public TCacheValue Update(string key, Func<TCacheValue, TCacheValue> updateValue) =>
            this.Update(key, updateValue, new UpdateItemConfig());

        /// <summary>
        /// Updates an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <returns><c>True</c> if the update operation was successfully, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// is null.
        /// </exception>
        public TCacheValue Update(string key, string region, Func<TCacheValue, TCacheValue> updateValue) =>
            this.Update(key, region, updateValue, new UpdateItemConfig());

        /// <summary>
        /// Updates an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        /// <param name="key">The key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <returns><c>True</c> if the update operation was successfully, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="updateValue"/> or <paramref name="config"/>
        /// is null.
        /// </exception>
        public TCacheValue Update(string key, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config)
        {
            TCacheValue value;
            this.TryUpdate(key, updateValue, config, out value);
            return value;
        }

        /// <summary>
        /// Updates an existing key in the cache.
        /// <para>
        /// The cache manager will make sure the update will always happen on the most recent version.
        /// </para>
        /// <para>
        /// If version conflicts occur, if for example multiple cache clients try to write the same
        /// key, and during the update process, someone else changed the value for the key, the
        /// cache manager will retry the operation.
        /// </para>
        /// <para>
        /// The <paramref name="updateValue"/> function will get invoked on each retry with the most
        /// recent value which is stored in cache.
        /// </para>
        /// </summary>
        /// <remarks>
        /// If the cache does not use a distributed cache system. Update is doing exactly the same
        /// as Get plus Put.
        /// </remarks>
        /// <param name="key">The key to update.</param>
        /// <param name="region">The region of the key to update.</param>
        /// <param name="updateValue">The function to perform the update.</param>
        /// <param name="config">The cache configuration used to specify the update behavior.</param>
        /// <returns><c>True</c> if the update operation was successfully, <c>False</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="key"/> or <paramref name="region"/> or <paramref name="updateValue"/>
        /// or <paramref name="config"/> is null.
        /// </exception>
        public TCacheValue Update(string key, string region, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config)
        {
            TCacheValue value;
            this.TryUpdate(key, region, updateValue, config, out value);
            return value;
        }

        /// <summary>
        /// Adds a value to the cache handles. Triggers OnAdd if the key has been added.
        /// </summary>
        /// <param name="item">The <c>CacheItem</c> to be added to the cache.</param>
        /// <returns>
        /// <c>true</c> if the key was not already added to the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">If item is null.</exception>
        protected internal override bool AddInternal(CacheItem<TCacheValue> item)
        {
            NotNull(item, nameof(item));

            var result = false;

            // also inverse it, so that the lowest level gets invoked first
            for (int handleIndex = this.cacheHandles.Length - 1; handleIndex >= 0; handleIndex--)
            {
                var handle = this.cacheHandles[handleIndex];
                if (AddItemToHandle(item, handle))
                {
                    result = true;
                }
                else
                {
                    // this means, the item exists already, maybe with a different value already
                    // lets evict the item from all other handles so that we might get a fresh copy
                    // whenever the item gets requested evict from other is more passive than adding
                    // the version which exists to all others lets have the user decide what to do
                    // when we return false...
                    // Note: we might also just have added the item to a cache handel a level below,
                    //       this will get removed, too!
                    this.EvictFromOtherHandles(item.Key, item.Region, handleIndex);
                    return false;
                }
            }

            // trigger only once and not per handle and only if the item was added!
            if (result)
            {
                this.TriggerOnAdd(item.Key, item.Region);
            }

            return result;
        }

        /// <summary>
        /// Puts a value into all cache handles. Triggers OnPut.
        /// </summary>
        /// <param name="item">The <c>CacheItem</c> to be added to the cache.</param>
        /// <exception cref="System.ArgumentNullException">If item is null.</exception>
        protected internal override void PutInternal(CacheItem<TCacheValue> item)
        {
            NotNull(item, nameof(item));

            foreach (var handle in this.cacheHandles)
            {
                if (handle.Configuration.EnableStatistics)
                {
                    // check if it is really a new item otherwise the items count is crap because we
                    // count it every time, but use only the current handle to retrieve the item,
                    // otherwise we would trigger gets and find it in another handle maybe
                    var oldItem = string.IsNullOrWhiteSpace(item.Region) ?
                        handle.GetCacheItem(item.Key) :
                        handle.GetCacheItem(item.Key, item.Region);

                    handle.Stats.OnPut(item, oldItem == null);
                }

                handle.Put(item);
            }

            // update back plate
            if (this.Configuration.HasBackPlate)
            {
                if (string.IsNullOrWhiteSpace(item.Region))
                {
                    this.cacheBackPlate.NotifyChange(item.Key);
                }
                else
                {
                    this.cacheBackPlate.NotifyChange(item.Key, item.Region);
                }
            }

            this.TriggerOnPut(item.Key, item.Region);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting
        /// unmanaged resources.
        /// </summary>
        /// <param name="disposeManaged">Indicates if the dispose should release managed resources.</param>
        protected override void Dispose(bool disposeManaged)
        {
            if (disposeManaged)
            {
                if (this.Configuration.HasBackPlate)
                {
                    this.cacheBackPlate.Dispose();
                }

                foreach (var handle in this.cacheHandles)
                {
                    handle.Dispose();
                }
            }

            base.Dispose(disposeManaged);
        }

        /// <summary>
        /// Gets the <c>CacheItem</c> for the specified key.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>The <c>CacheItem</c>.</returns>
        /// <exception cref="ArgumentNullException">If the <paramref name="key"/> is null.</exception>
        protected override CacheItem<TCacheValue> GetCacheItemInternal(string key) =>
            this.GetCacheItemInternal(key, null);

        /// <summary>
        /// Gets the <c>CacheItem</c> for the specified key and region.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>The <c>CacheItem</c>.</returns>
        /// <exception cref="ArgumentNullException">
        /// If the <paramref name="key"/> or <paramref name="region"/> is null.
        /// </exception>
        protected override CacheItem<TCacheValue> GetCacheItemInternal(string key, string region)
        {
            CacheItem<TCacheValue> cacheItem = null;

            for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
            {
                var handle = this.cacheHandles[handleIndex];
                if (string.IsNullOrWhiteSpace(region))
                {
                    cacheItem = handle.GetCacheItem(key);
                }
                else
                {
                    cacheItem = handle.GetCacheItem(key, region);
                }

                handle.Stats.OnGet(region);

                if (cacheItem != null)
                {
                    // update last accessed, might be used for custom sliding implementations
                    cacheItem.LastAccessedUtc = DateTime.UtcNow;

                    // update other handles if needed
                    this.AddToHandles(cacheItem, handleIndex);
                    handle.Stats.OnHit(region);
                    this.TriggerOnGet(key, region);
                    break;
                }
                else
                {
                    handle.Stats.OnMiss(region);
                }
            }

            return cacheItem;
        }

        /// <summary>
        /// Removes a value from the cache for the specified key.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <returns>
        /// <c>true</c> if the key was found and removed from the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="ArgumentNullException">If the <paramref name="key"/> is null.</exception>
        protected override bool RemoveInternal(string key) =>
            this.RemoveInternal(key, null);

        /// <summary>
        /// Removes a value from the cache for the specified key and region.
        /// </summary>
        /// <param name="key">The key being used to identify the item within the cache.</param>
        /// <param name="region">The cache region.</param>
        /// <returns>
        /// <c>true</c> if the key was found and removed from the cache, <c>false</c> otherwise.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// If the <paramref name="key"/> or <paramref name="region"/> is null.
        /// </exception>
        protected override bool RemoveInternal(string key, string region)
        {
            var result = false;

            foreach (var handle in this.cacheHandles)
            {
                var handleResult = false;
                if (!string.IsNullOrWhiteSpace(region))
                {
                    handleResult = handle.Remove(key, region);
                }
                else
                {
                    handleResult = handle.Remove(key);
                }

                if (handleResult)
                {
                    result = true;
                    handle.Stats.OnRemove(region);
                }
            }

            if (result)
            {
                // update back plate
                if (this.Configuration.HasBackPlate)
                {
                    if (string.IsNullOrWhiteSpace(region))
                    {
                        this.cacheBackPlate.NotifyRemove(key);
                    }
                    else
                    {
                        this.cacheBackPlate.NotifyRemove(key, region);
                    }
                }

                // trigger only once and not per handle
                this.TriggerOnRemove(key, region);
            }

            return result;
        }

        /// <summary>
        /// Evicts a cache item from <paramref name="handles"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        /// <param name="handles">The handles.</param>
        private static void EvictFromHandles(string key, string region, BaseCacheHandle<TCacheValue>[] handles)
        {
            foreach (var handle in handles)
            {
                EvictFromHandle(key, region, handle);
            }
        }

        private static void EvictFromHandle(string key, string region, BaseCacheHandle<TCacheValue> handle)
        {
            bool result;
            if (string.IsNullOrWhiteSpace(region))
            {
                result = handle.Remove(key);
            }
            else
            {
                result = handle.Remove(key, region);
            }

            if (result)
            {
                handle.Stats.OnRemove(region);
            }
        }

        private static bool AddItemToHandle(CacheItem<TCacheValue> item, BaseCacheHandle<TCacheValue> handle)
        {
            if (handle.Add(item))
            {
                handle.Stats.OnAdd(item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds or updates an item.
        /// </summary>
        /// <param name="item">The item to be added or updated.</param>
        /// <param name="updateValue">The update value function.</param>
        /// <param name="config">The configuration for updates.</param>
        /// <returns>The added or updated value.</returns>
        private TCacheValue AddOrUpdateInternal(CacheItem<TCacheValue> item, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config)
        {
            var tries = 0;
            do
            {
                tries++;
                TCacheValue returnValue;
                bool updated = string.IsNullOrWhiteSpace(item.Region) ?
                    this.TryUpdate(item.Key, updateValue, config, out returnValue) :
                    this.TryUpdate(item.Key, item.Region, updateValue, config, out returnValue);

                if (updated)
                {
                    return returnValue;
                }
                else
                {
                    // if the update didn't work, lets try to add it
                    if (this.AddInternal(item))
                    {
                        return item.Value;
                    }
                    //// Continue looping otherwise...
                    //// Add also didn't work, meaning the item is already there/someone added it in
                    //// the meantime, lets try it again...
                }
            }
            while (tries <= this.Configuration.MaxRetries);

            // exceeded max retries, failing the operation... (should not happen in 99,99% of the cases though, better throw?)
            return default(TCacheValue);
        }

        /// <summary>
        /// Adds an item to handles depending on the update mode configuration.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <param name="foundIndex">The index of the cache handle the item was found in.</param>
        private void AddToHandles(CacheItem<TCacheValue> item, int foundIndex)
        {
            switch (this.Configuration.CacheUpdateMode)
            {
                case CacheUpdateMode.None:
                    // do basically nothing
                    break;

                case CacheUpdateMode.Full:
                    // update all cache handles except the one where we found the item
                    for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
                    {
                        if (handleIndex != foundIndex)
                        {
                            this.cacheHandles[handleIndex].Add(item);
                        }
                    }

                    break;

                case CacheUpdateMode.Up:
                    // optimizing so we don't even have to iterate
                    if (foundIndex == 0)
                    {
                        break;
                    }

                    // update all cache handles with lower order, up the list
                    for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
                    {
                        if (handleIndex < foundIndex)
                        {
                            this.cacheHandles[handleIndex].Add(item);
                        }
                    }

                    break;
            }
        }

        private void AddToHandlesBelow(CacheItem<TCacheValue> item, int foundIndex)
        {
            if (item == null)
            {
                return;
            }

            for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
            {
                if (handleIndex > foundIndex)
                {
                    if (this.cacheHandles[handleIndex].Add(item))
                    {
                        this.cacheHandles[handleIndex].Stats.OnAdd(item);
                    }
                }
            }
        }

        /// <summary>
        /// Clears the cache handles provided.
        /// </summary>
        /// <param name="handles">The handles.</param>
        private void ClearHandles(BaseCacheHandle<TCacheValue>[] handles)
        {
            foreach (var handle in handles)
            {
                handle.Clear();
                handle.Stats.OnClear();
            }

            this.TriggerOnClear();
        }

        /// <summary>
        /// Invokes ClearRegion on the <paramref name="handles"/>.
        /// </summary>
        /// <param name="region">The region.</param>
        /// <param name="handles">The handles.</param>
        private void ClearRegionHandles(string region, BaseCacheHandle<TCacheValue>[] handles)
        {
            foreach (var handle in handles)
            {
                handle.ClearRegion(region);
                handle.Stats.OnClearRegion(region);
            }

            this.TriggerOnClearRegion(region);
        }

        /// <summary>
        /// Evicts a cache item from all cache handles except the one at <paramref name="excludeIndex"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        /// <param name="excludeIndex">Index of the exclude.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">If excludeIndex is not valid.</exception>
        private void EvictFromOtherHandles(string key, string region, int excludeIndex)
        {
            if (excludeIndex < 0 || excludeIndex >= this.cacheHandles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(excludeIndex));
            }

            for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
            {
                if (handleIndex != excludeIndex)
                {
                    EvictFromHandle(key, region, this.cacheHandles[handleIndex]);
                }
            }
        }

        private void EvictFromHandlesAbove(string key, string region, int excludeIndex)
        {
            if (excludeIndex < 0 || excludeIndex >= this.cacheHandles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(excludeIndex));
            }

            for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
            {
                if (handleIndex < excludeIndex)
                {
                    EvictFromHandle(key, region, this.cacheHandles[handleIndex]);
                }
            }
        }

        /// <summary>
        /// Sets the cache back plate and subscribes to it.
        /// </summary>
        /// <param name="backPlate">The back plate.</param>
        /// <exception cref="System.ArgumentNullException">
        /// If <paramref name="backPlate"/> is null.
        /// </exception>
        private void RegisterCacheBackPlate(CacheBackPlate backPlate)
        {
            NotNull(backPlate, nameof(backPlate));

            this.cacheBackPlate = backPlate;

            // TODO: better throw? Or at least log warn
            if (this.cacheHandles.Any(p => p.Configuration.IsBackPlateSource))
            {
                var handles = new Func<BaseCacheHandle<TCacheValue>[]>(() =>
                {
                    var handleList = new List<BaseCacheHandle<TCacheValue>>();
                    foreach (var handle in this.cacheHandles)
                    {
                        if (!handle.Configuration.IsBackPlateSource)
                        {
                            handleList.Add(handle);
                        }
                    }
                    return handleList.ToArray();
                });

                backPlate.SubscribeChanged((key) =>
                {
                    EvictFromHandles(key, null, handles());
                });

                backPlate.SubscribeChanged((key, region) =>
                {
                    EvictFromHandles(key, region, handles());
                });

                backPlate.SubscribeRemove((key) =>
                {
                    EvictFromHandles(key, null, handles());
                    this.TriggerOnRemove(key, null);
                });

                backPlate.SubscribeRemove((key, region) =>
                {
                    EvictFromHandles(key, region, handles());
                    this.TriggerOnRemove(key, region);
                });

                backPlate.SubscribeClear(() =>
                {
                    this.ClearHandles(handles());
                    this.TriggerOnClear();
                });

                backPlate.SubscribeClearRegion((region) =>
                {
                    this.ClearRegionHandles(region, handles());
                    this.TriggerOnClearRegion(region);
                });
            }
        }

        /// <summary>
        /// Triggers OnAdd.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        private void TriggerOnAdd(string key, string region)
        {
            if (this.OnAdd != null)
            {
                this.OnAdd(this, new CacheActionEventArgs(key, region));
            }
        }

        /// <summary>
        /// Triggers OnClear.
        /// </summary>
        private void TriggerOnClear()
        {
            if (this.OnClear != null)
            {
                this.OnClear(this, new CacheClearEventArgs());
            }
        }

        /// <summary>
        /// Triggers OnClearRegion.
        /// </summary>
        /// <param name="region">The region.</param>
        private void TriggerOnClearRegion(string region)
        {
            if (this.OnClearRegion != null)
            {
                this.OnClearRegion(this, new CacheClearRegionEventArgs(region));
            }
        }

        /// <summary>
        /// Triggers OnGet.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        private void TriggerOnGet(string key, string region)
        {
            if (this.OnGet != null)
            {
                this.OnGet(this, new CacheActionEventArgs(key, region));
            }
        }

        /// <summary>
        /// Triggers TriggerOnPut.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        private void TriggerOnPut(string key, string region)
        {
            if (this.OnPut != null)
            {
                this.OnPut(this, new CacheActionEventArgs(key, region));
            }
        }

        /// <summary>
        /// Triggers TriggerOnRemove.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        /// <exception cref="System.ArgumentNullException">If key is null.</exception>
        private void TriggerOnRemove(string key, string region)
        {
            NotNullOrWhiteSpace(key, nameof(key));

            if (this.OnRemove != null)
            {
                this.OnRemove(this, new CacheActionEventArgs(key, region));
            }
        }

        /// <summary>
        /// Triggers OnUpdate.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        /// <param name="config">The configuration.</param>
        /// <param name="result">The result.</param>
        private void TriggerOnUpdate(string key, string region, UpdateItemConfig config, UpdateItemResult<TCacheValue> result)
        {
            if (this.OnUpdate != null)
            {
                this.OnUpdate(this, new CacheUpdateEventArgs<TCacheValue>(key, region, config, result));
            }
        }

        /// <summary>
        /// Private implementation of Update.
        /// </summary>
        /// <param name="handles">The handles.</param>
        /// <param name="key">The key.</param>
        /// <param name="updateValue">The update value.</param>
        /// <param name="config">The configuration.</param>
        /// <param name="value">The value.</param>
        /// <returns><c>True</c> if the item has been updated.</returns>
        private bool UpdateInternal(
            BaseCacheHandle<TCacheValue>[] handles,
            string key,
            Func<TCacheValue, TCacheValue> updateValue,
            UpdateItemConfig config,
            out TCacheValue value) =>
            this.UpdateInternal(handles, key, null, updateValue, config, out value);

        /// <summary>
        /// Private implementation of Update.
        /// <para>
        /// Change: 6/6/15: inverted the handle loop so that the lowest gets updated first,
        /// Otherwise, it could happen that an in memory cache has the item and updates it, but the
        /// second handle doesn't have it Still, overall result would be true, but if the second
        /// handle is the back plate, the item would get flushed. If the item was updated
        /// successfully, If the manager is configured with CacheUpdateMode.None, we'll proceed,
        /// otherwise (up, or All), we'll flush all handles above the current one; the next get will
        /// add the items back.
        /// </para>
        /// </summary>
        /// <param name="handles">The handles.</param>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        /// <param name="updateValue">The update value.</param>
        /// <param name="config">The configuration.</param>
        /// <param name="value">The value.</param>
        /// <returns><c>True</c> if the item has been updated.</returns>
        private bool UpdateInternal(BaseCacheHandle<TCacheValue>[] handles, string key, string region, Func<TCacheValue, TCacheValue> updateValue, UpdateItemConfig config, out TCacheValue value)
        {
            UpdateItemResultState overallResult = UpdateItemResultState.Success;
            bool overallVersionConflictOccurred = false;
            int overallTries = 1;

            // assign null
            value = default(TCacheValue);

            if (handles.Length == 0)
            {
                return false;
            }

            // lowest level goes first...
            for (int handleIndex = handles.Length - 1; handleIndex >= 0; handleIndex--)
            {
                var handle = handles[handleIndex];

                UpdateItemResult<TCacheValue> result = string.IsNullOrWhiteSpace(region) ?
                    handle.Update(key, updateValue, config) :
                    handle.Update(key, region, updateValue, config);

                if (result.VersionConflictOccurred)
                {
                    overallVersionConflictOccurred = true;
                }

                overallResult = result.UpdateState;
                overallTries += result.NumberOfTriesNeeded > 1 ? result.NumberOfTriesNeeded - 1 : 0;

                if (result.UpdateState == UpdateItemResultState.Success)
                {
                    // only on success, the returned value will not be null
                    value = result.Value;
                    handle.Stats.OnUpdate(key, region, result);

                    // evict others, we don't know if the update on other handles could actually
                    // succeed... There is a risk the update on other handles could create a
                    // different version than we created with the first successful update... we can
                    // safely add the item to handles below us though.
                    this.EvictFromHandlesAbove(key, region, handleIndex);

                    var item = string.IsNullOrWhiteSpace(region) ? handle.GetCacheItem(key) : handle.GetCacheItem(key, region);
                    this.AddToHandlesBelow(item, handleIndex);
                    break;
                }
                else if (result.UpdateState != UpdateItemResultState.ItemDidNotExist)
                {
                    // only if the item does not exist in the current handle, we procceed the
                    // loop... otherwise, we had too many retries... this basically indicates an
                    // invalide state of the cache: The item is there, but we couldn't update it and
                    // it most likely has a different version
                    // TODO: logging
                    this.EvictFromOtherHandles(key, region, handleIndex);
                    break;
                }

                // TODO: revist this, but I think the version conflict handling was a mistake and leeds to errors. Default
                // was evict other handles, anyways, what we now always do
                //// if (result.VersionConflictOccurred && config.VersionConflictOperation != VersionConflictHandling.Ignore)
                //// {
                ////    switch (config.VersionConflictOperation)
                ////    {
                ////        // default behavior
                ////        case VersionConflictHandling.EvictItemFromOtherCaches:
                ////            this.EvictFromOtherHandles(key, region, handleIndex);
                ////            break;

                ////        // update other caches could potentially leed to inconsitency because we only use Put to update the handles...
                ////        case VersionConflictHandling.UpdateOtherCaches:
                ////            CacheItem<TCacheValue> item;
                ////            if (string.IsNullOrWhiteSpace(region))
                ////            {
                ////                item = handle.GetCacheItem(key);
                ////            }
                ////            else
                ////            {
                ////                item = handle.GetCacheItem(key, region);
                ////            }

                ////            this.UpdateOtherHandles(item, handleIndex);
                ////            break;
                ////    }

                ////    // stop loop because we already handled everything.
                ////    break;
                //// }
            }

            // update back plate
            if (overallResult == UpdateItemResultState.Success && this.Configuration.HasBackPlate)
            {
                if (string.IsNullOrWhiteSpace(region))
                {
                    this.cacheBackPlate.NotifyChange(key);
                }
                else
                {
                    this.cacheBackPlate.NotifyChange(key, region);
                }
            }

            // trigger update event with the overall results
            this.TriggerOnUpdate(key, region, config, new UpdateItemResult<TCacheValue>(value, overallResult, overallVersionConflictOccurred, overallTries));

            return overallResult == UpdateItemResultState.Success;
        }

        /// <summary>
        /// Updates all cache handles except the one at <paramref name="excludeIndex"/>.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="excludeIndex">Index of the exclude.</param>
        private void UpdateOtherHandles(CacheItem<TCacheValue> item, int excludeIndex)
        {
            if (item == null)
            {
                return;
            }

            // .Where(p => p.Key != excludeIndex).Select(p => p.Value)
            for (int handleIndex = 0; handleIndex < this.cacheHandles.Length; handleIndex++)
            {
                if (handleIndex != excludeIndex)
                {
                    this.cacheHandles[handleIndex].Put(item);
                    //// handle.Stats.OnPut(item); don't update,
                    //// we expect the item to be in the cache already at this point, so we should not increase the count...

                    this.TriggerOnPut(item.Key, item.Region);
                }
            }
        }
    }
}