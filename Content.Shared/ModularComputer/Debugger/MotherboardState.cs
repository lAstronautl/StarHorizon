using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Debugger;

[Serializable, NetSerializable]
public sealed class MotherboardState
{
    public readonly bool IsPowered;
    public readonly List<HartState> HartStates;
    public readonly List<MmioDeviceState> MmioDeviceStates;

    public MotherboardState(bool isPowered, List<HartState> hartStates, List<MmioDeviceState> mmioDeviceStates)
    {
        IsPowered = isPowered;
        HartStates = hartStates;
        MmioDeviceStates = mmioDeviceStates;
    }
}
