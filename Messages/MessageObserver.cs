using Messages.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Messages
{
    /// <summary>
    /// A single subscription: a filter, a handler and a private queue (ActionBlock)
    /// that delivers messages one at a time, in order, off the publisher's thread.
    /// The handler receives a token that is cancelled when the subscription stops.
    /// </summary>
    internal sealed class MessageObserver : IObserver<IBaseMessage>
    {
        private static int _lastId;

        private readonly Func<IBaseMessage, bool> _filter;
        private readonly Func<IBaseMessage, CancellationToken, Task> _handler;
        private readonly Action<int, IBaseMessage, Exception> _onHandlerError;
        private readonly Action _onCompleted;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ActionBlock<IBaseMessage> _block;

        public MessageObserver(
            Func<IBaseMessage, bool> filter,
            Func<IBaseMessage, CancellationToken, Task> handler,
            Action<int, IBaseMessage, Exception> onHandlerError,
            int boundedCapacity,
            Action onCompleted = null)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _onHandlerError = onHandlerError;
            _onCompleted = onCompleted;

            Id = Interlocked.Increment(ref _lastId);

            _block = new ActionBlock<IBaseMessage>(InvokeAsync, new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = boundedCapacity,
                CancellationToken = _cts.Token
            });
        }

        public int Id { get; }

        public bool IsActive => !_cts.IsCancellationRequested;

        public bool Accepts(IBaseMessage message) => _filter(message);

        /// <summary>Queues the message without waiting. False if the queue is full or the observer is stopped.</summary>
        public bool TryEnqueue(IBaseMessage message) => IsActive && _block.Post(message);

        /// <summary>Queues the message, waiting for space when the queue is bounded and full.</summary>
        public Task<bool> EnqueueAsync(IBaseMessage message, CancellationToken cancellationToken)
            => IsActive ? _block.SendAsync(message, cancellationToken) : Task.FromResult(false);

        /// <summary>Stops delivery: queued messages are discarded and the handler is not called again.</summary>
        public void Stop()
        {
            if (_cts.IsCancellationRequested) return;
            _cts.Cancel();
            _block.Complete();
        }

        private async Task InvokeAsync(IBaseMessage message)
        {
            if (_cts.IsCancellationRequested) return;

            try
            {
                await _handler(message, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Never let an exception escape: it would fault the ActionBlock
                // and silently kill this subscription.
                try
                {
                    _onHandlerError?.Invoke(Id, message, ex);
                }
                catch
                {
                    // An error callback that throws must not break delivery either.
                }
            }
        }

        #region IObserver<IBaseMessage>

        public void OnNext(IBaseMessage value) => TryEnqueue(value);

        public void OnError(Exception error) => OnCompleted();

        public void OnCompleted()
        {
            Stop();
            _onCompleted?.Invoke();
        }

        #endregion
    }
}
