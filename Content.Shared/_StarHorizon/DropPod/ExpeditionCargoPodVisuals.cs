using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>Капсула с грузом (волны врагов, приёмник): падает (скрыта), лежит закрытая, открыта.</summary>
[Serializable, NetSerializable]
public enum ExpeditionCargoPodVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum ExpeditionCargoPodStage : byte
{
    Falling,
    Landed,
    Open,
}
