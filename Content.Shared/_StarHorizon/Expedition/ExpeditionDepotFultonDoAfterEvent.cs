using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.Expedition;

/// <summary>Приёмник с собранными образцами привязан к фултону (действие «Привязать к фултону»).</summary>
[Serializable, NetSerializable]
public sealed partial class ExpeditionDepotFultonDoAfterEvent : SimpleDoAfterEvent
{
}
