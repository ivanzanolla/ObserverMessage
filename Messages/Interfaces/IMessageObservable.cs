using System;

namespace Messages.Interfaces
{
    internal interface IMessageObservable<TObserver, TMessage> : IObservable<TMessage>
        where TObserver : IMessageObserver<TMessage>
        where TMessage : IBaseMessage
    {
        void Publish(TMessage message);

    }
}