using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Programmer;

[Serializable, NetSerializable]
public sealed class ProgrammerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly NetEntity EntityUid;
    public readonly ProgrammerState State;
    public readonly long Limit;

    public ProgrammerBoundUserInterfaceState(NetEntity entityUid, ProgrammerState state, long limit)
    {
        EntityUid = entityUid;
        State = state;
        Limit = limit;
    }
}
