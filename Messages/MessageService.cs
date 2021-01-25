using Messages.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Messages
{
    public class MessageService : IMessageService
    {
        private readonly IMessageObservable<MessageObserver<IBaseMessage>, IBaseMessage> _messageObservable;
        private readonly IList<Unsubscriber<MessageObserver<IBaseMessage>, IBaseMessage>> _disposables;

        private MessageService()
        {
            _messageObservable = new MessageObservable<MessageObserver<IBaseMessage>, IBaseMessage>();
            _disposables = new List<Unsubscriber<MessageObserver<IBaseMessage>, IBaseMessage>>();
        }

        #region Singleton

        // laziness + thread safety
        private static readonly Lazy<MessageService> instance = new Lazy<MessageService>(() =>
        {
            return new MessageService();
        });


        public static MessageService Instance => instance.Value;

        #endregion


        public void Publish(IBaseMessage message)
        {
            _messageObservable.Publish(message);
        }


        public int Subscribe(Action<IBaseMessage> action, string systemName)
        {
            var messageObserver = new MessageObserver<IBaseMessage>(action, systemName);

            var disposable = _messageObservable.Subscribe(messageObserver);

            _disposables.Add((Unsubscriber<MessageObserver<IBaseMessage>, IBaseMessage>)disposable);

            return messageObserver.Id;

        }


        /// <summary>
        /// Dispose all
        /// </summary>
        public void UnSuscribe()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }


        /// <summary>
        /// Dispose specific observer
        /// </summary>
        /// <param name="id"></param>
        public void UnSuscribe(int id)
        {

            foreach (var disposable in _disposables)
            {
                disposable.Dispose(id);
            }
        }


    }




}
