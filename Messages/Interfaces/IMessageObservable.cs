using System;

namespace Messages.Interfaces
{
    internal interface IMessageObservable<Observer, Message>
        where Observer : IMessageObserver<Message>
        where Message : IBaseMessage
    {
        void Publish(Message message);
        IDisposable Subscribe(IObserver<Message> observer);
    }
}