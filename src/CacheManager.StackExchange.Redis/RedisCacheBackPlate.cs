﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CacheManager.Core;
using CacheManager.Core.Internal;
using CacheManager.Core.Logging;
using static CacheManager.Core.Utility.Guard;
using StackRedis = StackExchange.Redis;

namespace CacheManager.Redis
{
    /// <summary>
    /// Implementation of the cache back plate with Redis Pub/Sub feature.
    /// <para>
    /// Pub/Sub is used to send messages to the redis server on any Update, cache Clear, Region
    /// Clear or Remove operation. Every cache manager with the same configuration subscribes to the
    /// same chanel and can react on those messages to keep other cache handles in sync with the 'master'.
    /// </para>
    /// </summary>
    public sealed class RedisCacheBackPlate : CacheBackPlate
    {
        private readonly string channelName;
        private readonly string identifier;
        private readonly ILogger logger;
        private StackRedis.ISubscriber redisSubscriper;
        private Timer timer;
        private HashSet<string> messages = new HashSet<string>();
        private object messageLock = new object();
        private int skippedMessages = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisCacheBackPlate"/> class.
        /// </summary>
        /// <param name="configuration">The cache manager configuration.</param>
        /// <param name="loggerFactory">The logger factory</param>
        public RedisCacheBackPlate(CacheManagerConfiguration configuration, ILoggerFactory loggerFactory)
            : base(configuration)
        {
            NotNull(configuration, nameof(configuration));
            NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(this);
            this.channelName = configuration.BackPlateChannelName ?? "CacheManagerBackPlate";
            this.identifier = Guid.NewGuid().ToString();

            RetryHelper.Retry(
                () =>
                {
                    // throws an exception if not found for the name
                    var cfg = RedisConfigurations.GetConfiguration(this.ConfigurationKey);

                    var connection = RedisConnectionPool.Connect(cfg);

                    this.redisSubscriper = connection.GetSubscriber();
                },
                configuration.RetryTimeout,
                configuration.MaxRetries,
                this.logger);

            this.Subscribe();

            this.timer = new Timer(
                (obj) =>
                {
                    lock (this.messageLock)
                    {
                        try
                        {
                            if (this.messages != null && this.messages.Count > 0)
                            {
                                var msgs = string.Join(",", this.messages);
                                if (this.logger.IsEnabled(LogLevel.Debug))
                                {
                                    this.logger.LogDebug("Back-plate is sending {0} messages ({1} skipped).", this.messages.Count, this.skippedMessages);
                                }

                                this.Publish(msgs);
                                this.skippedMessages = 0;
                                this.messages.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogError(ex, "Error occurred sending back plate messages.");
                            throw;
                        }
                    }
                },
                this,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(100));
        }

        /// <summary>
        /// Notifies other cache clients about a changed cache key.
        /// </summary>
        /// <param name="key">The key.</param>
        public override void NotifyChange(string key)
        {
            this.PublishMessage(BackPlateMessage.ForChanged(this.identifier, key));
        }

        /// <summary>
        /// Notifies other cache clients about a changed cache key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        public override void NotifyChange(string key, string region)
        {
            this.PublishMessage(BackPlateMessage.ForChanged(this.identifier, key, region));
        }

        /// <summary>
        /// Notifies other cache clients about a cache clear.
        /// </summary>
        public override void NotifyClear()
        {
            this.PublishMessage(BackPlateMessage.ForClear(this.identifier));
        }

        /// <summary>
        /// Notifies other cache clients about a cache clear region call.
        /// </summary>
        /// <param name="region">The region.</param>
        public override void NotifyClearRegion(string region)
        {
            this.PublishMessage(BackPlateMessage.ForClearRegion(this.identifier, region));
        }

        /// <summary>
        /// Notifies other cache clients about a removed cache key.
        /// </summary>
        /// <param name="key">The key.</param>
        public override void NotifyRemove(string key)
        {
            this.PublishMessage(BackPlateMessage.ForRemoved(this.identifier, key));
        }

        /// <summary>
        /// Notifies other cache clients about a removed cache key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="region">The region.</param>
        public override void NotifyRemove(string key, string region)
        {
            this.PublishMessage(BackPlateMessage.ForRemoved(this.identifier, key, region));
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="managed">
        /// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release
        /// only unmanaged resources.
        /// </param>
        protected override void Dispose(bool managed)
        {
            if (managed)
            {
                this.redisSubscriper.Unsubscribe(this.channelName);
                this.timer.Dispose();
            }

            base.Dispose(managed);
        }

        private void Publish(string message)
        {
            this.redisSubscriper.Publish(this.channelName, message, StackRedis.CommandFlags.FireAndForget);
        }

        private void PublishMessage(BackPlateMessage message)
        {
            var msg = message.Serialize();

            lock (this.messageLock)
            {
                if (message.Action == BackPlateAction.Clear)
                {
                    Interlocked.Exchange(ref this.skippedMessages, this.messages.Count);
                    this.messages.Clear();
                }

                if (!this.messages.Add(msg))
                {
                    Interlocked.Increment(ref this.skippedMessages);
                    if (this.logger.IsEnabled(LogLevel.Trace))
                    {
                        this.logger.LogTrace("Skipped duplicate message: {0}.", msg);
                    }
                }
            }
        }

        private void Subscribe()
        {
            this.redisSubscriper.Subscribe(
                this.channelName,
                (channel, msg) =>
                {
                    var fullMessage = ((string)msg).Split(',')
                        .Where(s => !s.StartsWith(this.identifier, StringComparison.Ordinal))
                        .ToArray();

                    if (fullMessage.Length == 0)
                    {
                        // no messages for this instance
                        return;
                    }

                    if (this.logger.IsEnabled(LogLevel.Information))
                    {
                        this.logger.LogInfo("Back-plate got notified with {0} new messages.", fullMessage.Length);
                    }

                    foreach (var messageStr in fullMessage)
                    {
                        var message = BackPlateMessage.Deserialize(messageStr);

                        switch (message.Action)
                        {
                            case BackPlateAction.Clear:
                                this.OnClear();
                                break;

                            case BackPlateAction.ClearRegion:
                                this.OnClearRegion(message.Region);
                                break;

                            case BackPlateAction.Changed:
                                if (string.IsNullOrWhiteSpace(message.Region))
                                {
                                    this.OnChange(message.Key);
                                }
                                else
                                {
                                    this.OnChange(message.Key, message.Region);
                                }
                                break;

                            case BackPlateAction.Removed:
                                if (string.IsNullOrWhiteSpace(message.Region))
                                {
                                    this.OnRemove(message.Key);
                                }
                                else
                                {
                                    this.OnRemove(message.Key, message.Region);
                                }
                                break;
                        }
                    }
                },
                StackRedis.CommandFlags.FireAndForget);
        }
    }
}