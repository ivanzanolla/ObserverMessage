using Messages;
using Messages.Interfaces;
using System;

namespace ObserverMessage
{
    class Program
    {
        private static IMessageService _messageService => MessageService.Instance;

        static void Main(string[] args)
        {

            int id1 = _messageService.Subscribe(OnReceive1, Message.Name);
            int id2 = _messageService.Subscribe(OnReceive2, Message.Name);

            for (int i = 0; i < 10; i++)
            {
                var message = new Message
                {
                    Inc = i
                };

                _messageService.Publish(message);

            }


            _messageService.UnSuscribe(id1);
            _messageService.UnSuscribe(id2);
        }

        private static void OnReceive1(IBaseMessage obj)
        {
            if (obj.SystemName.Equals(Message.Name))
            {
                var msg = (Message)obj;

                System.Diagnostics.Debug.WriteLine($" {nameof(OnReceive1)} TimeStapm: {DateTime.Now.Millisecond} {msg.SystemName} {msg.Inc}");


            }
        }


        private static void OnReceive2(IBaseMessage obj)
        {
            if (obj.SystemName.Equals(Message.Name))
            {
                var msg = (Message)obj;

                System.Diagnostics.Debug.WriteLine($" {nameof(OnReceive2)} TimeStapm: {DateTime.Now.Millisecond} {msg.SystemName} {msg.Inc}");
            }
        }

  
    }
}
