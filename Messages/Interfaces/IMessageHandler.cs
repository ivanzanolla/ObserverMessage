using System.Threading;
using System.Threading.Tasks;

namespace Messages.Interfaces
{
    /// <summary>
    /// Handles messages of type <typeparamref name="TMessage"/>. Register it with
    /// <c>services.AddMessageHandler&lt;TMessage, THandler&gt;()</c>: a new instance is resolved
    /// in its own dependency injection scope for every message, so scoped services
    /// (for example a database context) are safe to inject and are disposed after each message.
    /// </summary>
    public interface IMessageHandler<in TMessage> where TMessage : IBaseMessage
    {
        /// <param name="message">The message to handle.</param>
        /// <param name="cancellationToken">Cancelled when the subscription stops, for example at application shutdown.</param>
        Task HandleAsync(TMessage message, CancellationToken cancellationToken);
    }
}
