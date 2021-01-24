using Messages;

namespace ObserverMessage
{
    public class Message : BaseMessage
    {
        public const string Name = "NuovoMessage";

        public int Inc { get; set; }
        public Message() : base(Name)
        {
        }
    }
}
