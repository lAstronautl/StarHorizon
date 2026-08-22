using Content.Shared._Horizon.StationDeployment;

namespace Content.Server._Horizon.StationDeployment.Components;

/// <summary>
/// Stores all active delivery orders for a deployed station.
/// </summary>
[RegisterComponent]
public sealed partial class StationOrderDatabaseComponent : Component
{
    [DataField]
    public List<StationOrderData> Orders = new();

    /// <summary>
    /// Used to determine unique order IDs.
    /// </summary>
    [DataField]
    public int TotalOrders;
}
