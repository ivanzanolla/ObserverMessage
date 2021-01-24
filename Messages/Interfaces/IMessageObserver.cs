namespace Messages.Interfaces
{
    public interface IMessageObserver<Message> where Message : IBaseMessage
    {
        int Id { get; }
        string SystemName { get; }

        void OnNext(Message message);
    }
}