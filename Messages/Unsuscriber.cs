using Messages.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Messages
{
    internal class Unsubscriber<Observer, Message> : IDisposable
       where Observer : IMessageObserver<Message>
       where Message : IBaseMessage
    {
        private readonly IList<Observer> _observers;
        private readonly Observer _observer;

        public Unsubscriber(List<Observer> observers, Observer observer)
        {
            _observers = observers;
            _observer = observer;
        }


        /// <summary>
        /// Dispose all the observer
        /// </summary>
        public void Dispose()
        {
            if (_observers.Contains(_observer))
                _observers.Remove(_observer);
        }


        /// <summary>
        /// Dispose specific observer
        /// </summary>
        /// <param name="id"></param>
        public void Dispose(int id)
        {
            var o = _observers.SingleOrDefault(o => o.Id == id);

            if (o != null)
            {
                _observers.Remove(o);
            }
        }
    }


}
