using System;

namespace ObserverMessage
{
    public sealed class HandlerErrorEventArgs : EventArgs
    {
        public HandlerErrorEventArgs(int subscriptionId, IBaseMessage message, Exception exception, Type handlerType = null)
        {
            SubscriptionId = subscriptionId;
            Message = message;
            Exception = exception;
            HandlerType = handlerType;
        }

        public int SubscriptionId { get; }
        public IBaseMessage Message { get; }
        public Exception Exception { get; }

        /// <summary>The handler class for subscriptions created by <c>AddMessageHandler</c>; null for delegate and <see cref="IObserver{T}"/> subscriptions.</summary>
        public Type HandlerType { get; }
    }

    public sealed class MessageDroppedEventArgs : EventArgs
    {
        public MessageDroppedEventArgs(int subscriptionId, IBaseMessage message, Type handlerType = null)
        {
            SubscriptionId = subscriptionId;
            Message = message;
            HandlerType = handlerType;
        }

        public int SubscriptionId { get; }
        public IBaseMessage Message { get; }

        /// <summary>The handler class for subscriptions created by <c>AddMessageHandler</c>; null for delegate and <see cref="IObserver{T}"/> subscriptions.</summary>
        public Type HandlerType { get; }
    }
}
