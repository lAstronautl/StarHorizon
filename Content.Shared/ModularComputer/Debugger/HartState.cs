using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Debugger;

[Serializable, NetSerializable]
public sealed class HartState
{
    public readonly Dictionary<ulong, ulong> Registers;

    public HartState(Dictionary<ulong, ulong> registers)
    {
        Registers = registers;
    }
}
