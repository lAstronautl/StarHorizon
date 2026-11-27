using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.Expedition;

/// <summary>Ремонт узла разбитого эвакуационного шаттла закончен.</summary>
[Serializable, NetSerializable]
public sealed partial class ExpeditionRepairDoAfterEvent : SimpleDoAfterEvent
{
}
