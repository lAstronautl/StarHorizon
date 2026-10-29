using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.GravityGun;

/// <summary>
/// A held item that drags grids with the mouse the way the admin griddrag command does.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GridDragGunComponent : Component
{
    /// <summary>
    /// How far from the user the grid may be dragged, in metres.
    /// </summary>
    [DataField]
    public float MaxDistance = 15f;
}
