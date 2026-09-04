using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Programmer;

[Serializable, NetSerializable]
public sealed class UploadBootromEvent : EntityEventArgs
{
    public readonly NetEntity EntityUid;
    public readonly byte[] Data;

    public UploadBootromEvent(NetEntity entityUid, byte[] data)
    {
        EntityUid = entityUid;
        Data = data;
    }
}
