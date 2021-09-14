﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Orleans.CodeGeneration;
using Orleans.Concurrency;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.Runtime;

using Microsoft.Extensions.DependencyInjection;

namespace Orleankka
{
    using Utility;

    public interface IStreamRef {}

    [Serializable, Immutable]
    [DebuggerDisplay("s->{ToString()}")]
    public class StreamRef<TItem> : IEquatable<StreamRef<TItem>>, IEquatable<StreamPath>, IStreamRef
    {
        [NonSerialized] readonly IStreamProvider provider;
        [NonSerialized] readonly IStreamRefMiddleware middleware;

        protected StreamRef(StreamPath path)
        {
            Path = path;
        }

        internal StreamRef(StreamPath path, IStreamProvider provider, IStreamRefMiddleware middleware) 
            : this(path)
        {

            this.provider = provider;
            this.middleware = middleware;
        }

        [NonSerialized]
        IAsyncStream<TItem> endpoint;
        IAsyncStream<TItem> Endpoint
        {
            get
            {
                if (endpoint != null)
                    return endpoint;
                
                if (provider == null)
                    throw new InvalidOperationException($"StreamRef [{Path}] has not been bound to runtime");

                return endpoint = provider.GetStream<TItem>(Guid.Empty, Path.Id);
            }
        }

        public StreamPath Path { get; }

        /// <summary>
        /// Publishes message to a stream
        /// <typeparam name="TMessage">
        /// The possible types of messages:
        /// <list type="table">
        ///     <listheader>
        ///         <term>Type</term>
        ///         <description>Description</description>
        ///     </listheader>
        ///     <item>
        ///         <term><see cref="NextItem{TItem}"/></term>
        ///         <description>
        ///             The next item to be published
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="NextItemBatch{T}"/></term>
        ///         <description>
        ///             The next batch of items to be published
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="NotifyStreamError"/></term>
        ///         <description>
        ///             Signals publisher error
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="NotifyStreamCompleted"/></term>
        ///         <description>
        ///             Signals that no more items will be produced on this stream
        ///         </description>
        ///     </item>
        /// </list>
        /// </typeparam>
        /// </summary>
        /// <param name="message">The message to be published</param>
        /// <returns>A Task that is completed when the message has been accepted</returns>
        public virtual async Task Publish<TMessage>(TMessage message) where TMessage : PublishMessage
        {
            switch (message)
            {
                case NextItem<TItem> next:
                    await middleware.Publish(Path, next, async x =>
                    {
                        await Endpoint.OnNextAsync(x.Item, x.Token);
                    });
                    break;
                case NextItemBatch<TItem> next:
                    await middleware.Publish(Path, next, async x =>
                    {
                        await Endpoint.OnNextBatchAsync(x.Items, x.Token);
                    });
                    break;
                case NotifyStreamError error:
                    await middleware.Publish(Path, error, async x =>
                    {
                        await Endpoint.OnErrorAsync(x.Exception);
                    });
                    break;
                case NotifyStreamCompleted completed:
                    await middleware.Publish(Path, completed, async _ =>
                    {
                        await Endpoint.OnCompletedAsync();
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(message), $"Unsupported type of publish message: '{message.GetType()}'");
            }
        }

        /// <summary>
        /// Subscribes consumer callback to receive messages published to a stream.
        /// </summary>
        /// <param name="callback">The callback delegate</param>
        /// <param name="options">The stream subscription options</param>
        /// <returns>
        /// <typeparam name="TOptions">The type of stream subscription options</typeparam>
        /// A promise for a <see cref="StreamSubscription{TItem}"/> that represents the subscription.
        /// The consumer may unsubscribe by using this object.
        /// The subscription remains active for as long as it is not explicitly unsubscribed.
        /// </returns>
        public virtual async Task<StreamSubscription<TItem>> Subscribe<TOptions>(Func<StreamMessage, Task> callback, TOptions options) 
            where TOptions : SubscribeOptions
        {
            Requires.NotNull(callback, nameof(callback));

            return options switch 
            {
                SubscribeReceiveBatch o => await SubscribeBatch(o), 
                SubscribeReceiveItem o => await Subscribe(o),
                _ => throw new ArgumentOutOfRangeException(nameof(options), 
                    $"Unsupported type of options: '{options.GetType()}'")
            };

            async Task<StreamSubscription<TItem>> Subscribe(SubscribeReceiveItem o)
            {
                var observer = CreateObserver(callback);
                
                var predicate = o.Filter != null
                    ? StreamFilter.Internal.Predicate
                    : (StreamFilterPredicate) null;

                var handle = await Endpoint.SubscribeAsync(observer, o.Token, predicate, o.Filter);
                return new StreamSubscription<TItem>(this, handle);
            }

            async Task<StreamSubscription<TItem>> SubscribeBatch(SubscribeReceiveBatch o)
            {
                var observer = CreateBatchObserver(callback);
                var handle = await Endpoint.SubscribeAsync(observer, o.Token);
                return new StreamSubscription<TItem>(this, handle);
            }
        }
        
        /// <summary>
        /// Returns a list of all current stream subscriptions.
        /// </summary>
        /// <returns> A promise for a list of StreamSubscription </returns>
        public virtual async Task<IList<StreamSubscription<TItem>>> Subscriptions()
        {
            var handles = await Endpoint.GetAllSubscriptionHandles();
            return handles.Select(x => new StreamSubscription<TItem>(this, x)).ToList();
        }

        public bool Equals(StreamRef<TItem> other)
        {
            return !ReferenceEquals(null, other) && (ReferenceEquals(this, other)
                    || Path.Equals(other.Path));
        }

        public override bool Equals(object obj)
        {
            return !ReferenceEquals(null, obj) && (ReferenceEquals(this, obj)
                    || obj.GetType() == GetType() && Equals((StreamRef<TItem>)obj));
        }

        public static implicit operator StreamPath(StreamRef<TItem> arg) => arg.Path;

        public bool Equals(StreamPath other) => Path.Equals(other);
        public override int GetHashCode() => Path.GetHashCode();

        public static bool operator ==(StreamRef<TItem> left, StreamRef<TItem> right) => Equals(left, right);
        public static bool operator !=(StreamRef<TItem> left, StreamRef<TItem> right) => !Equals(left, right);

        public override string ToString() => Path.ToString();

        #region Orleans Native Serialization

        [CopierMethod]
        static object Copy(object input, ICopyContext context) => input;

        [SerializerMethod]
        static void Serialize(object input, ISerializationContext context, Type expected)
        {
            var writer = context.StreamWriter;
            var @ref = (StreamRef<TItem>)input;
            writer.Write(@ref.Path);
        }

        [DeserializerMethod]
        static object Deserialize(Type t, IDeserializationContext context)
        {
            var reader = context.StreamReader;
            var path = StreamPath.Parse(reader.ReadString());
            var system = context.ServiceProvider.GetRequiredService<IActorSystem>();
            return system.StreamOf<TItem>(path);
        }

        #endregion

        internal BatchObserver CreateBatchObserver(Func<StreamMessage, Task> callback)
        {
            return new BatchObserver(this, callback, middleware);
        }

        internal Observer CreateObserver(Func<StreamMessage, Task> callback)
        {
            return new Observer(this, callback, middleware);
        }

        internal class BatchObserver : IAsyncBatchObserver<TItem>
        {
            readonly StreamRef<TItem> stream;
            readonly Func<StreamMessage, Task> callback;
            readonly IStreamRefMiddleware middleware;

            public BatchObserver(StreamRef<TItem> stream, Func<StreamMessage, Task> callback, IStreamRefMiddleware middleware)
            {
                this.stream = stream;
                this.callback = callback;
                this.middleware = middleware;
            }

            public Task OnNextAsync(IList<SequentialItem<TItem>> items) =>
                middleware.Receive(stream.Path, new StreamItemBatch<TItem>(stream, items), x => callback(x));

            public Task OnCompletedAsync() => 
                middleware.Receive(stream.Path, new StreamCompleted(stream), x => callback(x));

            public Task OnErrorAsync(Exception ex) => 
                middleware.Receive(stream.Path, new StreamError(stream, ex), x => callback(x));
        }

        internal class Observer : IAsyncObserver<TItem>
        {
            readonly StreamRef<TItem> stream;
            readonly Func<StreamMessage, Task> callback;
            readonly IStreamRefMiddleware middleware;

            public Observer(StreamRef<TItem> stream, Func<StreamMessage, Task> callback, IStreamRefMiddleware middleware)
            {
                this.stream = stream;
                this.callback = callback;
                this.middleware = middleware;
            }

            public Task OnNextAsync(TItem item, StreamSequenceToken token = null) =>
                middleware.Receive(stream.Path, new StreamItem<TItem>(stream, item, token), x => callback(x));

            public Task OnCompletedAsync() => 
                middleware.Receive(stream.Path, new StreamCompleted(stream), x => callback(x));

            public Task OnErrorAsync(Exception ex) => 
                middleware.Receive(stream.Path, new StreamError(stream, ex), x => callback(x));
        }
    }
}