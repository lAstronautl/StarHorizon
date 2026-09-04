using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.SerialTerminal;

[Serializable, NetSerializable]
public sealed class SerialTerminalSendTextMessage : BoundUserInterfaceMessage
{
    public readonly string Message;

    public SerialTerminalSendTextMessage(string message)
    {
        Message = message;
    }
}
