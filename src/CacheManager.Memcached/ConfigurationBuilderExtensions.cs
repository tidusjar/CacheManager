﻿using CacheManager.Memcached;
using static CacheManager.Core.Utility.Guard;

namespace CacheManager.Core
{
    /// <summary>
    /// Extensions for the configuration builder specific to the memcached cache handle.
    /// </summary>
    public static class ConfigurationBuilderExtensions
    {
        /// <summary>
        /// Adds a <see cref="MemcachedCacheHandle{TCacheValue}"/>. The <paramref name="configurationName"/> must match with cache configured via enyim configuration section.
        /// </summary>
        /// <param name="part">The builder part.</param>
        /// <param name="configurationName">The configuration name.</param>
        /// <returns>The part.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown if handleName is null.</exception>
        public static ConfigurationBuilderCacheHandlePart WithMemcachedCacheHandle(this ConfigurationBuilderCachePart part, string configurationName) =>
            WithMemcachedCacheHandle(part, configurationName, false);

        /// <summary>
        /// Adds a <see cref="MemcachedCacheHandle{TCacheValue}"/>. The <paramref name="configurationName"/> must match with cache configured via enyim configuration section.
        /// </summary>
        /// <param name="part">The builder part.</param>
        /// <param name="configurationName">The configuration name.</param>
        /// <param name="isBackPlateSource">
        /// Set this to true if this cache handle should be the source of the back plate.
        /// <para>This setting will be ignored if no back plate is configured.</para>
        /// </param>
        /// <returns>The part.</returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown if handleName or handleType are null.
        /// </exception>
        public static ConfigurationBuilderCacheHandlePart WithMemcachedCacheHandle(this ConfigurationBuilderCachePart part, string configurationName, bool isBackPlateSource)
        {
            NotNull(part, nameof(part));

            return part.WithHandle(typeof(MemcachedCacheHandle<>), configurationName, isBackPlateSource);
        }
    }
}