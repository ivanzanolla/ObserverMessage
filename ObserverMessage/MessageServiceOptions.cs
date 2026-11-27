using System;
using System.Threading.Tasks.Dataflow;

namespace ObserverMessage
{
    public sealed class MessageServiceOptions
    {
        private int _boundedCapacity = DataflowBlockOptions.Unbounded;

        /// <summary>
        /// Maximum number of messages queued per subscription (the one being handled included).
        /// Default: unbounded (-1). When a queue is full, <c>Publish</c> drops the message and raises
        /// <c>MessageDropped</c>, while <c>PublishAsync</c> waits for space.
        /// </summary>
        public int BoundedCapacity
        {
            get => _boundedCapacity;
            set
            {
                if (value != DataflowBlockOptions.Unbounded && value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "Use -1 (unbounded) or a value of at least 1.");
                _boundedCapacity = value;
            }
        }
    }
}
