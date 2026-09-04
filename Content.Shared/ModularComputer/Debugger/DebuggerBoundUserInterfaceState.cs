using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Debugger;

[Serializable, NetSerializable]
public sealed class DebuggerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly MotherboardState? MotherboardState;

    public DebuggerBoundUserInterfaceState(MotherboardState? motherboardState)
    {
        MotherboardState = motherboardState;
    }
}
