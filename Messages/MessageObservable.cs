using Messages.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Messages
{
    internal class MessageObservable<Observer, Message> : IObservable<Message>, IMessageObservable<Observer, Message> 
        where Observer : IMessageObserver<Message>
        where Message : IBaseMessage

    {
        private readonly List<Observer> _observers;



        public MessageObservable()
        {
            _observers = new List<Observer>();
        }

        public IDisposable Subscribe(IObserver<Message> observer)
        {
            var casted = (Observer)observer;

            if (!_observers.Contains(casted))
            {
                _observers.Add(casted);
            }

            return new Unsubscriber<Observer, Message>(_observers, casted);
        }

        public void Publish(Message message)
        {

            var observers = _observers.Where(o => o.SystemName.Equals(message.SystemName));

            Parallel.ForEach(observers, (observer) =>
            {
                observer.OnNext(message);
            });


        }
    }



}
