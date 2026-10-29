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
        if (TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var comp) || Transform(user).GridUid == grid)
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
