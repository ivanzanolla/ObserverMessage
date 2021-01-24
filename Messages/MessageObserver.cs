using Messages.Interfaces;
using System;
using System.Threading.Tasks.Dataflow;

namespace Messages
{
    internal class MessageObserver<Message> : IObserver<Message>, IMessageObserver<Message> 
        where Message : IBaseMessage
    {
        private readonly ActionBlock<Message> _actionBlock;

        private readonly string _systemName;
        public string SystemName => _systemName;


        public int Id { get; }

        private static int id = 1;

        public MessageObserver(Action<Message> action, string systemName)
        {
            _actionBlock = new ActionBlock<Message>(m => action(m));
            _systemName = systemName;
            Id = id++;
        }



        public void OnCompleted()
        {
            //
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(Message message)
        {
            _actionBlock.Post(message);
        }
    }
}
