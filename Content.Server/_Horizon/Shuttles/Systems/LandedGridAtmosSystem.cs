using System.Numerics;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Horizon.Shuttles.Components;
using Content.Shared.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.Shuttles.Systems;

/// <summary>
/// Open (space-type) tiles of a landable shuttle, e.g. lattice, have no air of their own. While they lie over a
/// landing pad they share the gas mixture of the pad tile underneath, so a person standing there is not in a vacuum
/// and gas is exchanged between both grids.
/// </summary>
public sealed class LandedGridAtmosSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextSync;
    private readonly List<Entity<MapGridComponent>> _gridsBuffer = new();
    private readonly List<TileAtmosphere> _tilesBuffer = new();

    /// <summary>Shuttle tiles currently showing pad air, per shuttle.</summary>
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _proxied = new();
    private readonly HashSet<Vector2i> _current = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSync)
            return;

        _nextSync = _timing.CurTime + SyncInterval;

        var query = EntityQueryEnumerator<LandableShuttleComponent, GridAtmosphereComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var atmos, out var grid, out var xform))
        {
            Sync(uid, atmos, grid, xform);
        }
    }

    private void Sync(EntityUid uid, GridAtmosphereComponent atmos, MapGridComponent grid, TransformComponent xform)
    {
        if (xform.MapUid is not { } mapUid || xform.MapID == MapId.Nullspace)
            return;

        _current.Clear();
        _tilesBuffer.Clear();
        _tilesBuffer.AddRange(atmos.MapTiles);

        foreach (var tile in _tilesBuffer)
        {
            var center = _map.GridTileToWorld(uid, grid, tile.GridIndices).Position;

            if (!TryGetPadUnder(uid, xform.MapID, center, out var padUid, out var padGrid))
                continue;

            var padTile = _map.WorldToTile(padUid, padGrid, center);
            if (_atmos.IsTileSpace(padUid, mapUid, padTile))
                continue;

            // Share the very same gas mixture with the pad tile (no copy) so gas really moves between both grids.
            if (!TryComp<GridAtmosphereComponent>(padUid, out var padAtmos) ||
                !padAtmos.Tiles.TryGetValue(padTile, out var padTileAtmos) ||
                padTileAtmos.Air is not { } air)
            {
                continue;
            }

            tile.Air = air;
            tile.AirArchived = padTileAtmos.AirArchived ?? air;
            tile.Space = false;
            _current.Add(tile.GridIndices);
        }

        // Tiles that are no longer over a pad go back to their normal (map) air.
        if (_proxied.TryGetValue(uid, out var previous))
        {
            foreach (var indices in previous)
            {
                if (_current.Contains(indices) || !atmos.Tiles.TryGetValue(indices, out var tile))
                    continue;

                // Stop sharing the pad's gas and fall back to the map atmosphere (space unless the map says otherwise).
                if (TryComp<MapAtmosphereComponent>(mapUid, out var mapAtmos))
                {
                    tile.Air = mapAtmos.Mixture;
                    tile.Space = mapAtmos.Space;
                }
                else
                {
                    tile.Air = GasMixture.SpaceGas;
                    tile.Space = true;
                }

                tile.AirArchived = tile.Air.Clone();
                _atmos.InvalidateTile((uid, atmos), indices);
            }
        }

        _proxied[uid] = new HashSet<Vector2i>(_current);
    }

    private bool TryGetPadUnder(EntityUid self, MapId mapId, Vector2 worldPos, out EntityUid padUid, out MapGridComponent padGrid)
    {
        padUid = default;
        padGrid = default!;

        var box = Box2.CenteredAround(worldPos, new Vector2(0.5f, 0.5f));
        _gridsBuffer.Clear();
        _mapManager.FindGridsIntersecting(mapId, box, ref _gridsBuffer);

        foreach (var other in _gridsBuffer)
        {
            if (other.Owner == self || !HasComp<LandingPadComponent>(other.Owner))
                continue;

            padUid = other.Owner;
            padGrid = other.Comp;
            return true;
        }

        return false;
    }
}
