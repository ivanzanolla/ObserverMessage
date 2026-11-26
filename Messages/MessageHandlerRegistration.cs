using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Messages
{
    /// <summary>One <c>AddMessageHandler</c> call, subscribed to the bus when the container creates it.</summary>
    internal abstract class MessageHandlerRegistration
    {
        public abstract int SubscribeTo(IMessageService bus, IServiceScopeFactory scopes);
    }

    internal sealed class MessageHandlerRegistration<TMessage, THandler> : MessageHandlerRegistration
        where TMessage : IBaseMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        public override int SubscribeTo(IMessageService bus, IServiceScopeFactory scopes)
            => bus.Subscribe<TMessage>(async (message, cancellationToken) =>
            {
                // CreateAsyncScope, not CreateScope: a scope holding an IAsyncDisposable-only
                // service throws when it is disposed synchronously.
                var scope = scopes.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                    await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                }
            });
    }
}
