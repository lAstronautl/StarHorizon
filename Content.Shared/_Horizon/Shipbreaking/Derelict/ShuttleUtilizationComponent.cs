using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Shipbreaking.Derelict;

/// <summary>
/// A marker, no data: the grid is a shuttle that is being utilized (a wreck bought at the derelict yard, or a piece
/// cut off one). Given to both grids when one splits. The utilization tools work only with such grids, and the yard
/// deletes them when the order is over.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShuttleUtilizationComponent : Component;
