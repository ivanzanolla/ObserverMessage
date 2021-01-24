using Messages.Interfaces;

namespace Messages
{
    public abstract class BaseMessage : IBaseMessage
    {
        protected BaseMessage(string sysName)
        {
            SystemName = sysName;
        }


        public string SystemName { get; protected set; }

    }
}
