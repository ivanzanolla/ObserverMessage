using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messages.Interfaces
{
    /// <summary>
    /// In-process event bus. Handlers run off the publisher's thread; each subscription
    /// receives its messages one at a time, in publishing order.
    /// </summary>
    public interface IMessageService : IObservable<IBaseMessage>, IDisposable
    {
        /// <summary>Raised when a handler throws. The subscription stays active.</summary>
        event EventHandler<HandlerErrorEventArgs> HandlerError;

        /// <summary>Raised when <see cref="Publish"/> finds a bounded subscription queue full.</summary>
        event EventHandler<MessageDroppedEventArgs> MessageDropped;

        /// <summary>Queues the message for every matching subscription and returns at once.</summary>
        void Publish(IBaseMessage message);

        /// <summary>Queues the message, waiting for space in bounded subscription queues.</summary>
        Task PublishAsync(IBaseMessage message, CancellationToken cancellationToken = default);

        /// <summary>Subscribes to the messages whose <see cref="IBaseMessage.SystemName"/> equals <paramref name="systemName"/>.</summary>
        int Subscribe(Action<IBaseMessage> handler, string systemName);

        /// <inheritdoc cref="Subscribe(Action{IBaseMessage}, string)"/>
        int Subscribe(Func<IBaseMessage, Task> handler, string systemName);

        /// <summary>Subscribes to every message of type <typeparamref name="TMessage"/> (or derived).</summary>
        int Subscribe<TMessage>(Action<TMessage> handler) where TMessage : IBaseMessage;

        /// <inheritdoc cref="Subscribe{TMessage}(Action{TMessage})"/>
        int Subscribe<TMessage>(Func<TMessage, Task> handler) where TMessage : IBaseMessage;

        /// <summary>
        /// Subscribes to every message of type <typeparamref name="TMessage"/> (or derived).
        /// The token is cancelled when this subscription stops, so a long-running handler can end early.
        /// </summary>
        int Subscribe<TMessage>(Func<TMessage, CancellationToken, Task> handler) where TMessage : IBaseMessage;

        /// <summary>Stops a subscription; its queued messages are discarded. False if the id is unknown.</summary>
        bool Unsubscribe(int subscriptionId);

        /// <summary>Stops every subscription.</summary>
        void UnsubscribeAll();
    }
}
