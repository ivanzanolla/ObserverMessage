using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ObserverMessage
{
    /// <summary>
    /// The subject of the Observer pattern: keeps the observers and forwards each
    /// published message to the ones whose filter accepts it.
    /// </summary>
    internal sealed class MessageObservable : IObservable<IBaseMessage>
    {
        private readonly ConcurrentDictionary<int, MessageObserver> _observers =
            new ConcurrentDictionary<int, MessageObserver>();

        private readonly int _boundedCapacity;
        private readonly Action<int, IBaseMessage, Exception, Type> _onHandlerError;
        private readonly Action<int, IBaseMessage, Type> _onDropped;

        public MessageObservable(
            int boundedCapacity,
            Action<int, IBaseMessage, Exception, Type> onHandlerError,
            Action<int, IBaseMessage, Type> onDropped)
        {
            _boundedCapacity = boundedCapacity;
            _onHandlerError = onHandlerError;
            _onDropped = onDropped;
        }

        public int Count => _observers.Count;

        public MessageObserver Add(Func<IBaseMessage, bool> filter, Func<IBaseMessage, CancellationToken, Task> handler, Action onCompleted = null, Type handlerType = null)
        {
            var observer = new MessageObserver(filter, handler, _onHandlerError, _boundedCapacity, onCompleted, handlerType);
            _observers[observer.Id] = observer;
            return observer;
        }

        /// <summary>Subscribes any <see cref="IObserver{T}"/> (for example an Rx observer) to every message.</summary>
        public IDisposable Subscribe(IObserver<IBaseMessage> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            var added = Add(
                _ => true,
                (message, _) =>
                {
                    observer.OnNext(message);
                    return Task.CompletedTask;
                },
                observer.OnCompleted);

            return new Unsubscriber(this, added.Id);
        }

        /// <summary>Stops and removes one observer. False if the id is unknown or already removed.</summary>
        public bool Remove(int id)
        {
            if (!_observers.TryRemove(id, out var observer)) return false;
            observer.Stop();
            return true;
        }

        public void RemoveAll()
        {
            foreach (var id in _observers.Keys)
            {
                Remove(id);
            }
        }

        /// <summary>Removes every observer and tells each one that the sequence has ended.</summary>
        public void CompleteAll()
        {
            foreach (var id in _observers.Keys)
            {
                if (_observers.TryRemove(id, out var observer))
                {
                    observer.OnCompleted();
                }
            }
        }

        public void Publish(IBaseMessage message)
        {
            foreach (var observer in Matching(message))
            {
                if (!observer.TryEnqueue(message) && observer.IsActive)
                {
                    _onDropped?.Invoke(observer.Id, message, observer.HandlerType);
                }
            }
        }

        public Task PublishAsync(IBaseMessage message, CancellationToken cancellationToken)
        {
            var sends = new List<Task>();
            foreach (var observer in Matching(message))
            {
                sends.Add(observer.EnqueueAsync(message, cancellationToken));
            }
            return Task.WhenAll(sends);
        }

        private IEnumerable<MessageObserver> Matching(IBaseMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            foreach (var pair in _observers)
            {
                if (pair.Value.Accepts(message))
                {
                    yield return pair.Value;
                }
            }
        }
    }
}
