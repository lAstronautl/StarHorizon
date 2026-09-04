using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public enum KeyState : byte
{
    Up = 0,
    Down = 1,
    Repeat = 2
}
