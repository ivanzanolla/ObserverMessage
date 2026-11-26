using Messages.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace ObserverMessage
{
    class Program
    {
        static void Main(string[] args)
        {
            var services = new ServiceCollection();
            services.AddObserverMessage();

            using var provider = services.BuildServiceProvider();
            var messageService = provider.GetRequiredService<IMessageService>();

            messageService.HandlerError += (_, e) =>
                Console.WriteLine($" Handler {e.SubscriptionId} failed: {e.Exception.Message}");

            // Routing by type: no casts needed in the handler.
            int id1 = messageService.Subscribe<Message>(OnReceive1);

            // Routing by SystemName.
            int id2 = messageService.Subscribe(OnReceive2, Message.Name);

            for (int i = 0; i < 1000; i++)
            {
                messageService.Publish(new Message { Inc = i });
            }

            Console.ReadLine();

            messageService.Unsubscribe(id1);
            messageService.Unsubscribe(id2);
        }

        private static void OnReceive1(Message msg)
        {
            Console.WriteLine($" {nameof(OnReceive1)} Timestamp: {DateTime.Now:HH:mm:ss.fff} {msg.SystemName} {msg.Inc}");
        }

        private static void OnReceive2(IBaseMessage obj)
        {
            var msg = (Message)obj;

            // A failing handler is reported through HandlerError and keeps receiving.
            if (msg.Inc == 500) throw new InvalidOperationException("demo error on message 500");

            Console.WriteLine($" {nameof(OnReceive2)} Timestamp: {DateTime.Now:HH:mm:ss.fff} {msg.SystemName} {msg.Inc}");
        }
    }
}
