using Messages.Interfaces;
using System;

namespace Messages
{
    public sealed class HandlerErrorEventArgs : EventArgs
    {
        public HandlerErrorEventArgs(int subscriptionId, IBaseMessage message, Exception exception)
        {
            SubscriptionId = subscriptionId;
            Message = message;
            Exception = exception;
        }

        public int SubscriptionId { get; }
        public IBaseMessage Message { get; }
        public Exception Exception { get; }
    }

    public sealed class MessageDroppedEventArgs : EventArgs
    {
        public MessageDroppedEventArgs(int subscriptionId, IBaseMessage message)
        {
            SubscriptionId = subscriptionId;
            Message = message;
        }

        public int SubscriptionId { get; }
        public IBaseMessage Message { get; }
    }
}
