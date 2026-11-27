using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace ObserverMessage
{
    /// <summary>One <c>AddMessageHandler</c> call, subscribed to the bus when the container creates it.</summary>
    internal abstract class MessageHandlerRegistration
    {
        public abstract int SubscribeTo(MessageService bus, IServiceScopeFactory scopes);
    }

    internal sealed class MessageHandlerRegistration<TMessage, THandler> : MessageHandlerRegistration
        where TMessage : IBaseMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        public override int SubscribeTo(MessageService bus, IServiceScopeFactory scopes)
            => bus.Subscribe<TMessage>(async (message, cancellationToken) =>
            {
                // CreateAsyncScope, not CreateScope: a scope holding an IAsyncDisposable-only
                // service throws when it is disposed synchronously.
                AsyncServiceScope scope;
                try
                {
                    scope = scopes.CreateAsyncScope();
                }
                catch (ObjectDisposedException)
                {
                    // The container is shutting down (it marks itself disposed before it disposes the bus,
                    // and singletons created after the bus are disposed first). Like cancellation, that is
                    // a clean stop, not a handler failure: drop the message.
                    return;
                }

                await using (scope.ConfigureAwait(false))
                {
                    var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                    await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }, typeof(THandler));
    }
}
