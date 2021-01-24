using System;

namespace Messages.Interfaces
{
    public interface IMessageService
    {
        void Publish(IBaseMessage message);
        int Subscribe(Action<IBaseMessage> action, string systemName);
        void UnSuscribe();
        void UnSuscribe(int id);
    }
}