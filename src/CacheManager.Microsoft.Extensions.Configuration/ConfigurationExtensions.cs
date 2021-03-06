﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CacheManager.Core.Internal;
using Microsoft.Extensions.Configuration;

namespace CacheManager.Core
{
    public static class ConfigurationExtensions
    {
        private const string CacheManagersSection = "cacheManagers";
        private const string RedisSection = "redis";
        private const string SerializerSection = "serializer";
        private const string LoggerFactorySection = "loggerFactory";
        private const string HandlesSection = "handles";
        private const string ConfigurationKey = "key";
        private const string ConfigurationName = "name";
        private const string ConfigurationType = "type";
        private const string ConfigurationKnownType = "knownType";
        private const string TypeJsonCacheSerializer = "CacheManager.Serialization.Json.JsonCacheSerializer, CacheManager.Serialization.Json";
        private const string TypeMicrosoftLoggerFactory = "CacheManager.Logging.MicrosoftLoggerFactoryAdapter, CacheManager.Microsoft.Extensions.Logging";
        private const string TypeRedisBackPlate = "CacheManager.Redis.RedisCacheBackPlate, CacheManager.StackExchange.Redis";
        private const string TypeSystemRuntimeHandle = "CacheManager.SystemRuntimeCaching.MemoryCacheHandle`1, CacheManager.SystemRuntimeCaching";
        private const string TypeSystemWebHandle = "CacheManager.Web.SystemWebCacheHandle`1, CacheManager.Web";
        private const string TypeRedisHandle = "CacheManager.Redis.RedisCacheHandle`1, CacheManager.StackExchange.Redis";
        private const string TypeCouchbaseHandle = "CacheManager.Couchbase.BucketCacheHandle`1, CacheManager.Couchbase";
        private const string TypeMemcachedHandle = "CacheManager.Memcached.MemcachedCacheHandle`1, CacheManager.Memcached";
        private const string TypeRedisConfiguration = "CacheManager.Redis.RedisConfiguration, CacheManager.StackExchange.Redis";
        private const string TypeRedisConfigurations = "CacheManager.Redis.RedisConfigurations, CacheManager.StackExchange.Redis";

        /// <summary>
        /// Gets the first cacheManager (can be used if only one configuration is specified, so no name is needed...).
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static CacheManagerConfiguration GetCacheConfiguration(this IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            configuration.LoadRedisConfigurations();

            var managersSection = configuration.GetSection(CacheManagersSection);
            if (managersSection.GetChildren().Count() == 0)
            {
                throw new InvalidOperationException(
                    $"No '{CacheManagersSection}' section found in the configuration provided.");
            }

            if (managersSection.GetChildren().Count() > 1)
            {
                throw new InvalidOperationException(
                    $"The '{CacheManagersSection}' section has more than one configuration defined. Please specifiy which one to load by name.");
            }

            return GetFromConfiguration(managersSection.GetChildren().First());
        }

        /// <summary>
        /// Tries to retrieve a <see cref="CacheManagerConfiguration"/> from the provided <see cref="IConfiguration"/> element.
        /// The <paramref name="configuration"/> should contain a <code>cacheManagers</code> section.
        /// See
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static CacheManagerConfiguration GetCacheConfiguration(this IConfiguration configuration, string name)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            configuration.LoadRedisConfigurations();

            var managersSection = configuration.GetSection(CacheManagersSection);
            if (managersSection.GetChildren().Count() > 0)
            {
                return GetByName(managersSection, name);
            }

            throw new InvalidOperationException($"No '{CacheManagersSection}' section found in the configuration provided.");
        }

        public static IEnumerable<CacheManagerConfiguration> GetCacheConfigurations(this IConfiguration configuration)
        {
            configuration.LoadRedisConfigurations();

            var managersSection = configuration.GetSection(CacheManagersSection);
            if (managersSection.GetChildren().Count() == 0)
            {
                throw new InvalidOperationException($"No '{CacheManagersSection}' section found in the configuration provided.");
            }

            foreach (var managerConfig in managersSection.GetChildren())
            {
                yield return GetFromConfiguration(managerConfig);
            }
        }

        public static void LoadRedisConfigurations(this IConfiguration configuration)
        {
            // load redis configurations if available
            if (configuration.GetSection(RedisSection).GetChildren().Count() > 0)
            {
                try
                {
                    var redisConfigurationType = Type.GetType(TypeRedisConfiguration, true);
                    var redisConfigurationsType = Type.GetType(TypeRedisConfigurations, true);

                    var addRedisConfiguration = redisConfigurationsType
                        .GetTypeInfo()
                        .DeclaredMethods
                        .FirstOrDefault(
                            p => p.Name == "AddConfiguration" &&
                            p.GetParameters().Length == 1 &&
                            p.GetParameters().First().ParameterType == redisConfigurationType);

                    if (addRedisConfiguration == null)
                    {
                        throw new InvalidOperationException("RedisConfigurations type might have changed or cannot be invoked.");
                    }

                    foreach (var redisConfig in configuration.GetSection(RedisSection).GetChildren())
                    {
                        if (string.IsNullOrWhiteSpace(redisConfig[ConfigurationKey]))
                        {
                            throw new InvalidOperationException(
                                $"Key is required in redis configuration but is not configured in '{redisConfig.Path}'.");
                        }

                        if (string.IsNullOrWhiteSpace(redisConfig["connectionString"]) &&
                            redisConfig.GetSection("endpoints").GetChildren().Count() == 0)
                        {
                            throw new InvalidOperationException(
                                $"Either connection string or endpoints must be configured in '{redisConfig.Path}' for a redis connection.");
                        }

                        var redis = redisConfig.Get(redisConfigurationType);
                        addRedisConfiguration.Invoke(null, new object[] { redis });
                    }
                }
                catch (FileNotFoundException ex)
                {
                    throw new InvalidOperationException(
                        "Redis types could not be loaded. Make sure that you have the CacheManager.Redis.Stackexchange package installed.",
                        ex);
                }
                catch (TypeLoadException ex)
                {
                    throw new InvalidOperationException(
                        "Redis types could not be loaded. Make sure that you have the CacheManager.Redis.Stackexchange package installed.",
                        ex);
                }
            }
        }

        private static CacheManagerConfiguration GetByName(IConfiguration configuration, string name)
        {
            var section = configuration.GetChildren().FirstOrDefault(p => p[ConfigurationName] == name);
            if (section == null)
            {
                throw new InvalidOperationException(
                    $"CacheManager configuration for name '{name}' not found.");
            }

            return GetFromConfiguration(section);
        }

        private static CacheManagerConfiguration GetFromConfiguration(IConfigurationSection configuration)
        {
            var managerConfiguration = configuration.Get<CacheManagerConfiguration>();

            var handlesConfiguration = configuration.GetSection(HandlesSection);

            if (handlesConfiguration.GetChildren().Count() == 0)
            {
                throw new InvalidOperationException(
                    $"No cache handles defined in '{configuration.Path}'.");
            }

            foreach (var handleConfiguration in handlesConfiguration.GetChildren())
            {
                var cacheHandleConfiguration = GetHandleFromConfiguration(handleConfiguration);
                managerConfiguration.CacheHandleConfigurations.Add(cacheHandleConfiguration);
            }

            GetBackPlateConfiguration(managerConfiguration, configuration);
            GetLoggerFactoryConfiguration(managerConfiguration, configuration);
            GetSerializerConfiguration(managerConfiguration, configuration);

            return managerConfiguration;
        }

        private static CacheHandleConfiguration GetHandleFromConfiguration(IConfigurationSection handleConfiguration)
        {
            var type = handleConfiguration[ConfigurationType];
            var knownType = handleConfiguration[ConfigurationKnownType];
            var key = handleConfiguration[ConfigurationKey] ?? handleConfiguration[ConfigurationName];    // name fallback for key
            var name = handleConfiguration[ConfigurationName];

            var cacheHandleConfiguration = handleConfiguration.Get<CacheHandleConfiguration>();
            cacheHandleConfiguration.Key = key;
            cacheHandleConfiguration.Name = name ?? cacheHandleConfiguration.Name;

            if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(knownType))
            {
                throw new InvalidOperationException(
                    $"No '{ConfigurationType}' or '{ConfigurationKnownType}' defined in cache handle configuration '{handleConfiguration.Path}'.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                var keyRequired = false;
                cacheHandleConfiguration.HandleType = GetKnownHandleType(knownType, handleConfiguration.Path, out keyRequired);

                // some handles require name or key to be set to link to other parts of the configuration
                // lets check if that condition is satisfied
                if (keyRequired && string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException(
                        $@"Known handle of type '{knownType}' requires '{ConfigurationKey}' or '{ConfigurationName}' to be defined.
                            Check configuration at '{handleConfiguration.Path}'.");
                }
            }
            else
            {
                cacheHandleConfiguration.HandleType = Type.GetType(type, true);
            }

            return cacheHandleConfiguration;
        }

        private static Type GetKnownHandleType(string knownTypeName, string path, out bool keyRequired)
        {
            keyRequired = false;
            try
            {
                switch (knownTypeName.ToLowerInvariant())
                {
                    case "systemruntime":
                        return Type.GetType(TypeSystemRuntimeHandle, true);

                    case "dictionary":
                        return typeof(DictionaryCacheHandle<>);

                    case "systemweb":
                        return Type.GetType(TypeSystemWebHandle, true);

                    case "redis":
                        keyRequired = true;
                        return Type.GetType(TypeRedisHandle, true);

                    case "couchbase":
                        keyRequired = true;
                        return Type.GetType(TypeCouchbaseHandle, true);

                    case "memcached":
                        keyRequired = true;
                        return Type.GetType(TypeMemcachedHandle, true);
                }
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException(
                    $"Type for '{ConfigurationKnownType}' '{knownTypeName}' could not be loaded. Make sure you have installed the corresponding NuGet package.",
                    ex);
            }
            catch (TypeLoadException ex)
            {
                throw new InvalidOperationException(
                    $"Type for '{ConfigurationKnownType}' '{knownTypeName}' could not be loaded. Make sure you have installed the corresponding NuGet package.",
                    ex);
            }

            throw new InvalidOperationException(
                $"Known handle type '{knownTypeName}' is invalid. Check configuration at '{path}'.");
        }

        private static void GetBackPlateConfiguration(CacheManagerConfiguration managerConfiguration, IConfigurationSection configuration)
        {
            var backPlateSection = configuration.GetSection("backPlate");
            if (backPlateSection.GetChildren().Count() == 0)
            {
                // no backplate
                return;
            }

            var type = backPlateSection[ConfigurationType];
            var knownType = backPlateSection[ConfigurationKnownType];
            var key = backPlateSection[ConfigurationKey];
            var channelName = backPlateSection["channelName"];

            if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(knownType))
            {
                throw new InvalidOperationException(
                    $"No '{ConfigurationType}' or '{ConfigurationKnownType}' defined in back plate configuration '{backPlateSection.Path}'.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                var keyRequired = false;
                managerConfiguration.BackPlateType = GetKnownBackPlateType(knownType, backPlateSection.Path, out keyRequired);
                if (keyRequired && string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException(
                        $"The key property is required for the '{knownType}' back plate, but is not configured in '{backPlateSection.Path}'.");
                }
            }
            else
            {
                managerConfiguration.BackPlateType = Type.GetType(type, true);
            }

            managerConfiguration.BackPlateChannelName = channelName;
            managerConfiguration.BackPlateConfigurationKey = key;
        }

        private static Type GetKnownBackPlateType(string knownTypeName, string path, out bool keyRequired)
        {
            switch (knownTypeName.ToLowerInvariant())
            {
                case "redis":
                    keyRequired = true;
                    return Type.GetType(TypeRedisBackPlate, true);
            }

            throw new InvalidOperationException(
                $"Known back-plate type '{knownTypeName}' is invalid. Check configuration at '{path}'.");
        }

        private static void GetLoggerFactoryConfiguration(CacheManagerConfiguration managerConfiguration, IConfigurationSection configuration)
        {
            var loggerFactorySection = configuration.GetSection(LoggerFactorySection);

            if (loggerFactorySection.GetChildren().Count() == 0)
            {
                // no logger factory
                return;
            }

            var knownType = loggerFactorySection[ConfigurationKnownType];
            var type = loggerFactorySection[ConfigurationType];

            if (string.IsNullOrWhiteSpace(knownType) && string.IsNullOrWhiteSpace(type))
            {
                throw new InvalidOperationException(
                    $"No '{ConfigurationType}' or '{ConfigurationKnownType}' defined in logger factory configuration '{loggerFactorySection.Path}'.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                managerConfiguration.LoggerFactoryType = GetKnownLoggerFactoryType(knownType, loggerFactorySection.Path);
            }
            else
            {
                managerConfiguration.LoggerFactoryType = Type.GetType(type, true);
            }
        }

        private static Type GetKnownLoggerFactoryType(string knownTypeName, string path)
        {
            switch (knownTypeName.ToLowerInvariant())
            {
                case "microsoft":
                    return Type.GetType(TypeMicrosoftLoggerFactory, true);
            }

            throw new InvalidOperationException(
                $"Known logger factory type '{knownTypeName}' is invalid. Check configuration at '{path}'.");
        }

        private static void GetSerializerConfiguration(CacheManagerConfiguration managerConfiguration, IConfigurationSection configuration)
        {
            var serializerSection = configuration.GetSection(SerializerSection);

            if (serializerSection.GetChildren().Count() == 0)
            {
                // no serializer
                return;
            }

            var knownType = serializerSection[ConfigurationKnownType];
            var type = serializerSection[ConfigurationType];

            if (string.IsNullOrWhiteSpace(knownType) && string.IsNullOrWhiteSpace(type))
            {
                throw new InvalidOperationException(
                    $"No '{ConfigurationType}' or '{ConfigurationKnownType}' defined in serializer configuration '{serializerSection.Path}'.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                managerConfiguration.SerializerType = GetKnownSerializerType(knownType, serializerSection.Path);
            }
            else
            {
                managerConfiguration.SerializerType = Type.GetType(type, true);
            }
        }

        private static Type GetKnownSerializerType(string knownTypeName, string path)
        {
            switch (knownTypeName.ToLowerInvariant())
            {
                case "binary":
#if DOTNET5_4 || DNXCORE50
                    throw new InvalidOperationException("BinaryCacheSerializer is not available on this platform");
#else
                    return typeof(BinaryCacheSerializer);
#endif
                case "json":
                    return Type.GetType(TypeJsonCacheSerializer, true);
            }

            throw new InvalidOperationException(
                $"Known serializer type '{knownTypeName}' is invalid. Check configuration at '{path}'.");
        }
    }
}