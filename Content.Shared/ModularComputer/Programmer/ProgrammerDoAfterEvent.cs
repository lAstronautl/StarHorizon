using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Programmer;

[Serializable, NetSerializable]
public sealed partial class ProgrammerDoAfterEvent : SimpleDoAfterEvent
{
}
