using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public readonly record struct KeyArgs(
    ScreenKey Key,
    bool IsRepeat,
    bool Alt,
    bool Control,
    bool Shift,
    bool System,
    int ScanCode);
