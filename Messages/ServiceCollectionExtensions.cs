using Messages;
using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Linq;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ObserverMessageServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the event bus as a singleton <see cref="IMessageService"/>.
        /// Safe to call any number of times: the bus is registered once, and every
        /// <paramref name="configure"/> callback is applied to its options in registration order,
        /// so a later callback overrides an earlier one. The callbacks run when the bus is first resolved,
        /// not at registration, so an invalid value throws then. The container disposes
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
                var bus = new MessageService(options);

                var scopes = sp.GetRequiredService<IServiceScopeFactory>();
                foreach (var registration in sp.GetServices<MessageHandlerRegistration>())
                    registration.SubscribeTo(bus, scopes);
                return bus;
            });
            return services;
        }

        /// <summary>
        /// Subscribes <typeparamref name="THandler"/> to <typeparamref name="TMessage"/>. For every message
        /// a new dependency injection scope is created and the handler is resolved from it, so scoped
        /// services are safe to inject and are disposed once the message is handled. Also registers the bus
        /// (see <see cref="AddObserverMessage"/>). Registering the same pair twice has no effect.
        /// <para>
        /// If an <see cref="IMessageService"/> is already registered before <c>AddObserverMessage</c> or
        /// <c>AddMessageHandler</c> is called, the bus factory is skipped and the handlers are not attached to it.
        /// An existing registration of <typeparamref name="THandler"/> is kept (<c>TryAddScoped</c>): if you
        /// register it as a singleton yourself, the per-message instance is lost.
        /// </para>
        /// </summary>
        public static IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services)
            where TMessage : IBaseMessage
            where THandler : class, IMessageHandler<TMessage>
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddObserverMessage();
            services.TryAddScoped<THandler>();

            var alreadyRegistered = services.Any(d =>
                d.ServiceType == typeof(MessageHandlerRegistration)
                && d.ImplementationInstance is MessageHandlerRegistration<TMessage, THandler>);
            if (!alreadyRegistered)
                services.AddSingleton<MessageHandlerRegistration>(new MessageHandlerRegistration<TMessage, THandler>());

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
