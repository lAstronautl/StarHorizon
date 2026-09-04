using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.SerialTerminal;

[Serializable, NetSerializable]
public sealed class SerialTerminalBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<string> Content;

    public SerialTerminalBoundUserInterfaceState(List<string> content)
    {
        Content = content;
    }
}
