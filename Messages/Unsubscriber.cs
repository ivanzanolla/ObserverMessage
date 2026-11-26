using System;

namespace Messages
{
    /// <summary>Token returned by <see cref="MessageObservable.Subscribe"/>: disposing it stops the subscription.</summary>
    internal sealed class Unsubscriber : IDisposable
    {
        private readonly MessageObservable _observable;
        private readonly int _id;

        public Unsubscriber(MessageObservable observable, int id)
        {
            _observable = observable;
            _id = id;
        }

        public void Dispose() => _observable.Remove(_id);
    }
}
