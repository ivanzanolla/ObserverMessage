using Messages.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Messages
{
    internal class MessageObservable<TObserver, TMessage> : IMessageObservable<TObserver, TMessage> 
        where TObserver : IMessageObserver<TMessage>
        where TMessage : IBaseMessage

    {
        private readonly List<TObserver> _observers;



        public MessageObservable()
        {
            _observers = new List<TObserver>();
        }

        public IDisposable Subscribe(IObserver<TMessage> observer)
        {
            var casted = (TObserver)observer;

            if (!_observers.Contains(casted))
            {
                _observers.Add(casted);
            }

            return new Unsubscriber<TObserver, TMessage>(_observers, casted);
        }

        public void Publish(TMessage message)
        {

            var observers = _observers.Where(o => o.SystemName.Equals(message.SystemName));

            Parallel.ForEach(observers, (observer) =>
            {
                observer.OnNext(message);
            });


        }
    }



}
