using System;

namespace Messages.Interfaces
{
    public interface IMessageObserver<TMessage> : IObserver<TMessage>
        where TMessage : IBaseMessage
    {
        int Id { get; }
        string SystemName { get; }
    }
}