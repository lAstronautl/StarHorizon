using Content.Client.Markers;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Horizon.Shuttles;

/// <summary>
/// Lowers the draw depth of sprites on other grids that lie under a landable shuttle so the shuttle floor drawn by
/// <see cref="LandedGridFloorOverlay"/> covers them. The original depth is restored once they are no longer covered.
/// </summary>
public sealed class LandedGridOcclusionSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<SpriteComponent> _spriteQuery;
    private EntityQuery<MarkerComponent> _markerQuery;
    private EntityQuery<ProjectileComponent> _projectileQuery;
    private EntityQuery<ThrownItemComponent> _thrownQuery;

    private const int CoveredDrawDepth = (int) DrawDepth.BelowFloor;
    private const float MinOverlapArea = 0.08f;

    /// <summary>Entities pushed under the shuttle floor, with their original draw depth.</summary>
    private readonly Dictionary<EntityUid, int> _covered = new();
    private readonly HashSet<EntityUid> _shouldCover = new();
    private readonly List<EntityUid> _restoreBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _markerQuery = GetEntityQuery<MarkerComponent>();
        _projectileQuery = GetEntityQuery<ProjectileComponent>();
        _thrownQuery = GetEntityQuery<ThrownItemComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        _shouldCover.Clear();

        var query = EntityQueryEnumerator<LandableShuttleComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var shuttleUid, out _, out var grid, out var xform))
        {
            if (xform.MapID == MapId.Nullspace)
                continue;

            var worldAabb = _transform.GetWorldMatrix(xform).TransformBox(grid.LocalAABB).Enlarged(0.05f);
            foreach (var ent in _lookup.GetEntitiesIntersecting(xform.MapID, worldAabb,
                         LookupFlags.Approximate | LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                if (ShouldCoverUnder(ent, shuttleUid, grid, xform))
                    _shouldCover.Add(ent);
            }
        }

        foreach (var uid in _shouldCover)
        {
            if (_covered.ContainsKey(uid) || !_spriteQuery.TryComp(uid, out var sprite) || !sprite.Visible)
                continue;

            _covered[uid] = sprite.DrawDepth;

            if (sprite.DrawDepth > CoveredDrawDepth)
                _sprite.SetDrawDepth((uid, sprite), CoveredDrawDepth);
        }

        _restoreBuffer.Clear();
        foreach (var (uid, originalDepth) in _covered)
        {
            if (_shouldCover.Contains(uid))
                continue;

            _restoreBuffer.Add(uid);

            if (!Deleted(uid) && _spriteQuery.TryComp(uid, out var sprite))
                _sprite.SetDrawDepth((uid, sprite), originalDepth);
        }

        foreach (var uid in _restoreBuffer)
            _covered.Remove(uid);
    }

    private bool ShouldCoverUnder(EntityUid ent, EntityUid shuttleUid, MapGridComponent grid, TransformComponent shuttleXform)
    {
        if (ent == shuttleUid || Deleted(ent) || !_spriteQuery.HasComp(ent))
            return false;

        if (_markerQuery.HasComp(ent) || _projectileQuery.HasComp(ent) || _thrownQuery.HasComp(ent))
            return false;

        var xform = Transform(ent);

        // Only sprites of other grids go under the floor; everything aboard the shuttle stays on top.
        if (xform.GridUid is not { } otherGrid || otherGrid == shuttleUid || xform.ParentUid == shuttleUid)
            return false;

        // Don't push down other landable shuttles stacked on this one.
        if (HasComp<LandableShuttleComponent>(otherGrid))
            return false;

        var entAabb = _lookup.GetWorldAABB(ent, xform);
        var localAabb = _transform.GetInvWorldMatrix(shuttleXform).TransformBox(entAabb);
        var overlapArea = 0f;

        foreach (var tileRef in _map.GetLocalTilesIntersecting(shuttleUid, grid, localAabb, ignoreEmpty: true))
        {
            var inter = localAabb.Intersect(_lookup.GetLocalBounds(tileRef, grid.TileSize));
            if (inter.IsEmpty())
                continue;

            overlapArea += inter.Width * inter.Height;
            if (overlapArea >= MinOverlapArea)
                return true;
        }

        return false;
    }
}
