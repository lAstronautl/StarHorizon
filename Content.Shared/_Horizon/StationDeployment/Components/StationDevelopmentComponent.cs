using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Horizon.StationDeployment.Components;

/// <summary>
/// Tracks a deployed station's development progress per category, raised by completing
/// station orders. Server-authoritative bookkeeping, consumed only via the task console's BUI state.
/// </summary>
[RegisterComponent]
public sealed partial class StationDevelopmentComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<TechDisciplinePrototype>, int> Progress = new();

    /// <summary>
    /// How many completed orders of one category are needed per level-up.
    /// </summary>
    [DataField]
    public int OrdersPerLevel = 3;
}
