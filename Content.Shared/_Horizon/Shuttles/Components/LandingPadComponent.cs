using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Shuttles.Components;

/// <summary>
/// Grid (e.g. a station hangar) that other grids with <see cref="LandableShuttleComponent"/> may fly onto and land on.
/// The tiles of the two grids may overlap, walls and other entities still collide.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LandingPadComponent : Component
{
    /// <summary>
    /// Landing zone in the local coordinates of this grid. If null, the whole grid is a landing zone.
    /// </summary>
    [DataField]
    public Box2? Zone;
}
