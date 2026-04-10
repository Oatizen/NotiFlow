using CommunityToolkit.Mvvm.Messaging.Messages;

namespace NotiFlow.Models
{
    public class BarragePreviewMessage : ValueChangedMessage<string>
    {
        public BarragePreviewMessage(string message) : base(message)
        {
        }
    }
}
