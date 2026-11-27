using Messages.Interfaces;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Messages
{
    /// <summary>
    /// In-process event bus built on the Observer pattern.
    /// Use one instance per application: register it with
    /// <c>services.AddObserverMessage()</c> and inject <see cref="IMessageService"/>.
    /// </summary>
    public sealed class MessageService : IMessageService
    {
        private readonly MessageObservable _observable;

        public MessageService() : this(new MessageServiceOptions())
        {
        }

        public MessageService(MessageServiceOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _observable = new MessageObservable(options.BoundedCapacity, RaiseHandlerError, RaiseMessageDropped);
        }

        public event EventHandler<HandlerErrorEventArgs> HandlerError;

        public event EventHandler<MessageDroppedEventArgs> MessageDropped;

        public void Publish(IBaseMessage message) => _observable.Publish(message);

        public Task PublishAsync(IBaseMessage message, CancellationToken cancellationToken = default)
            => _observable.PublishAsync(message, cancellationToken);

        public int Subscribe(Action<IBaseMessage> handler, string systemName)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Subscribe(Sync(handler), systemName);
        }

        public int Subscribe(Func<IBaseMessage, Task> handler, string systemName)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (systemName == null) throw new ArgumentNullException(nameof(systemName));

            return _observable.Add(m => string.Equals(m.SystemName, systemName, StringComparison.Ordinal), (m, _) => handler(m)).Id;
        }

        public int Subscribe<TMessage>(Action<TMessage> handler) where TMessage : IBaseMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Subscribe(Sync(handler));
        }

        public int Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : IBaseMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return Subscribe<TMessage>((m, _) => handler(m));
        }

        public int Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : IBaseMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return Subscribe(handler, null);
        }

        /// <summary>Subscribes a handler class registered through <c>AddMessageHandler</c>, so errors and drops can name it.</summary>
        internal int Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler, Type handlerType) where TMessage : IBaseMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            return _observable.Add(m => m is TMessage, (m, ct) => handler((TMessage)m, ct), handlerType: handlerType).Id;
        }

        /// <summary>Subscribes any <see cref="IObserver{T}"/> to every message. Dispose the result to stop.</summary>
        public IDisposable Subscribe(IObserver<IBaseMessage> observer) => _observable.Subscribe(observer);

        public bool Unsubscribe(int subscriptionId) => _observable.Remove(subscriptionId);

        public void UnsubscribeAll() => _observable.RemoveAll();

        /// <summary>
        /// Stops every subscription and calls <see cref="IObserver{T}.OnCompleted"/> on the observers
        /// subscribed through <see cref="Subscribe(IObserver{IBaseMessage})"/>. The service stays usable.
        /// </summary>
        public void Dispose() => _observable.CompleteAll();

        private static Func<T, Task> Sync<T>(Action<T> action) => m =>
        {
            action(m);
            return Task.CompletedTask;
        };

        private static string Describe(int subscriptionId, Type handlerType)
            => handlerType == null
                ? $"subscription {subscriptionId}"
                : $"handler {handlerType.Name} (subscription {subscriptionId})";

        private void RaiseHandlerError(int subscriptionId, IBaseMessage message, Exception exception, Type handlerType)
        {
            var handler = HandlerError;
            if (handler == null)
            {
                Trace.TraceError($"ObserverMessage: {Describe(subscriptionId, handlerType)} failed on '{message.SystemName}': {exception}");
                return;
            }
            handler(this, new HandlerErrorEventArgs(subscriptionId, message, exception, handlerType));
        }

        private void RaiseMessageDropped(int subscriptionId, IBaseMessage message, Type handlerType)
        {
            var handler = MessageDropped;
            if (handler == null)
            {
                Trace.TraceWarning($"ObserverMessage: {Describe(subscriptionId, handlerType)} queue full, dropped '{message.SystemName}'.");
                return;
            }
            handler(this, new MessageDroppedEventArgs(subscriptionId, message, handlerType));
        }
    }
}
