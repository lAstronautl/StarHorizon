using System.Numerics;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shuttles.Systems;

/// <summary>
/// Walls are static bodies, and static bodies never collide with each other, so once the grid-vs-grid
/// collision is turned off (see <see cref="ShuttleLandingSystem"/>) walls of a shuttle and a landing pad
/// would pass through each other. This system adds simple box "hull" fixtures to the grid bodies themselves
/// (which can be dynamic) for every tile with an impassable anchored entity:
/// shuttle hull (layer ShuttleHull) collides with pad hull (layer PadHull) only.
/// </summary>
public sealed class LandingHullSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly FixtureSystem _fixtures = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public const string HullPrefix = "landing_hull_";
    private const float HalfSize = 0.49f;

    private static readonly int HullLayer = (int) (CollisionGroup.Impassable | CollisionGroup.MidImpassable |
                                                    CollisionGroup.HighImpassable | CollisionGroup.LowImpassable);
    private static readonly int HullMask = (int) CollisionGroup.FullTileMask;


    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _dirty = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LandableShuttleComponent, ComponentStartup>((uid, _, _) => MarkGridDirty(uid));
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorChanged);
    }

    private void MarkGridDirty(EntityUid uid)
    {
        if (!TryComp<MapGridComponent>(uid, out var grid))
            return;

        foreach (var tile in _map.GetAllTiles(uid, grid))
            _dirty.Add((uid, tile.GridIndices));
    }

    private void OnAnchorChanged(ref AnchorStateChangedEvent args)
    {
        var xform = args.Transform;
        if (xform.GridUid is not { } gridUid || !IsHullGrid(gridUid))
            return;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        _dirty.Add((gridUid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates)));
    }

    private bool IsHullGrid(EntityUid uid) => HasComp<LandableShuttleComponent>(uid);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_dirty.Count == 0)
            return;

        foreach (var (gridUid, tile) in _dirty)
        {
            UpdateTile(gridUid, tile);
        }

        _dirty.Clear();
    }

    private void UpdateTile(EntityUid gridUid, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<PhysicsComponent>(gridUid, out var body) ||
            !TryComp<FixturesComponent>(gridUid, out var manager))
        {
            return;
        }

        var id = $"{HullPrefix}{tile.X}_{tile.Y}";
        var blocked = false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (!TryComp<FixturesComponent>(ent, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
                break;
        }

        var exists = manager.Fixtures.ContainsKey(id);

        if (!blocked)
        {
            if (exists)
                _fixtures.DestroyFixture(gridUid, id, manager: manager, body: body);
            return;
        }

        if (exists)
            return;

        var shape = new PolygonShape();
        shape.SetAsBox(HalfSize, HalfSize, new Vector2(tile.X + 0.5f, tile.Y + 0.5f), 0f);

        // Same idea as a vehicle bumper: a hull fixture on the (dynamic) grid body collides with the static wall
        // entities of other grids, while the grid floors themselves pass through each other.
        _fixtures.TryCreateFixture(gridUid, shape, id,
            density: 0f,
            hard: true,
            collisionLayer: HullLayer,
            collisionMask: HullMask,
            manager: manager,
            body: body);
    }
}
