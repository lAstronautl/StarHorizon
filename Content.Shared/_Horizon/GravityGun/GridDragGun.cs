using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map.Components;

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

    /// <summary>
    /// A grid with more tiles than this can not be taken.
    /// </summary>
    [DataField]
    public int MaxTiles = 5;

    /// <summary>
    /// The gun takes only grids that have a <see cref="Shipbreaking.Derelict.ShuttleUtilizationComponent"/>.
    /// </summary>
    [DataField]
    public bool RequireUtilization;

    /// <summary>
    /// How hard the grid is pulled to the cursor, in metres per second per metre of distance.
    /// </summary>
    [DataField]
    public float Gain = 4f;

    /// <summary>
    /// How much of the way from its speed to the wanted speed the grid goes with each request, 0 to 1. The smaller it
    /// is, the more the grid lags behind the cursor and carries on after it.
    /// </summary>
    [DataField]
    public float Blend = 0.08f;

    [DataField]
    public float MaxSpeed = 20f;

    /// <summary>
    /// The colour of the beam and of the grid while it is moved, the same as of the tether gun.
    /// </summary>
    [DataField]
    public Color LineColor = Color.Orange;
}

/// <summary>
/// On a grid while a <see cref="GridDragGunComponent"/> moves it, so everybody sees the beam and the grid coloured.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GridDraggedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Gun;

    /// <summary>
    /// When the gun last asked for the grid to be moved. Only the server needs it.
    /// </summary>
    public TimeSpan LastRequest;
}

/// <summary>
/// What both sides agree on: which grids the gun may take.
/// </summary>
public abstract class SharedGridDragGunSystem : EntitySystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;

    /// <summary>
    /// A grid is taken only if it is small enough, nobody alive is on it, and the user is not standing on it.
    /// </summary>
    public bool CanGrab(EntityUid user, GridDragGunComponent gun, EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var comp) || Transform(user).GridUid == grid ||
            (gun.RequireUtilization && !HasComp<Shipbreaking.Derelict.ShuttleUtilizationComponent>(grid)))
            return false;

        var tiles = 0;
        foreach (var _ in _maps.GetAllTiles(grid, comp))
        {
            if (++tiles > gun.MaxTiles)
                return false;
        }

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out var mob, out var xform))
        {
            if (xform.GridUid == grid && _mobState.IsAlive(uid, mob))
                return false;
        }

        return true;
    }
}
