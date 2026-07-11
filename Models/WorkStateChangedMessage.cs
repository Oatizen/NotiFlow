using CommunityToolkit.Mvvm.Messaging.Messages;

namespace NotiFlow.Models
{
    public class WorkStateChangedMessage : ValueChangedMessage<bool>
    {
        public WorkStateChangedMessage(bool isWorking) : base(isWorking)
        {
        }
    }
}
