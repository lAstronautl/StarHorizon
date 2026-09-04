using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Debugger;

[Serializable, NetSerializable]
public sealed class MmioDeviceState
{
    public readonly int Id;
    public readonly string Label;
    public readonly ulong Address;
    public readonly ulong Size;

    public MmioDeviceState(int id, string label, ulong address, ulong size)
    {
        Id = id;
        Label = label;
        Address = address;
        Size = size;
    }
}
