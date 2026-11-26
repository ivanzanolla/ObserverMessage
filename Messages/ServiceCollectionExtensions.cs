using Messages;
using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ObserverMessageServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the event bus as a singleton <see cref="IMessageService"/>.
        /// Calling it more than once keeps the first registration. The container disposes
        /// the bus on shutdown, which stops every subscription.
        /// </summary>
        public static IServiceCollection AddObserverMessage(
            this IServiceCollection services,
            Action<MessageServiceOptions> configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            var options = new MessageServiceOptions();
            configure?.Invoke(options);

            services.TryAddSingleton<IMessageService>(_ => new MessageService(options));
            return services;
        }
    }
}
