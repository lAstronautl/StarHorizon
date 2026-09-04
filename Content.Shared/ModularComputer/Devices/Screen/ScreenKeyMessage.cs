using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public sealed class ScreenKeyMessage : BoundUserInterfaceMessage
{
    public readonly KeyArgs KeyArgs;
    public readonly KeyState State;

    public ScreenKeyMessage(KeyArgs keyArgs, KeyState state)
    {
        KeyArgs = keyArgs;
        State = state;
    }
}
