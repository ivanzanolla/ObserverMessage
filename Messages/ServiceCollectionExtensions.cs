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
        /// Safe to call any number of times: the bus is registered once, and every
        /// <paramref name="configure"/> callback is applied to its options in registration order,
        /// so a later callback overrides an earlier one. The container disposes
        /// the bus on shutdown, which stops every subscription.
        /// </summary>
        public static IServiceCollection AddObserverMessage(
            this IServiceCollection services,
            Action<MessageServiceOptions> configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            if (configure != null)
                services.AddSingleton(new ObserverMessageConfigurator(configure));

            services.TryAddSingleton<IMessageService>(sp =>
            {
                var options = new MessageServiceOptions();
                foreach (var configurator in sp.GetServices<ObserverMessageConfigurator>())
                    configurator.Apply(options);
                return new MessageService(options);
            });
            return services;
        }
    }

    internal sealed class ObserverMessageConfigurator
    {
        private readonly Action<MessageServiceOptions> _configure;

        public ObserverMessageConfigurator(Action<MessageServiceOptions> configure) => _configure = configure;

        public void Apply(MessageServiceOptions options) => _configure(options);
    }
}
