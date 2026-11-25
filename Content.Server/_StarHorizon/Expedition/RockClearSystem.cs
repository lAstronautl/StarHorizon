using System.Linq;
using System.Numerics;
using Content.Server.Gatherable.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Выбор места посадки дроп-подов: тайл без породы (стены, которые добываются киркой: <see cref="GatherableComponent"/>)
/// и других твёрдых объектов, и не замурованный карман: из него должен быть выход на большое открытое пространство.
/// </summary>
public sealed class RockClearSystem : EntitySystem
{
    [Dependency] private readonly MapSystem _map = default!;

    /// <summary>На тайле нет ни породы, ни других твёрдых объектов: сюда можно садиться (поды с мобами в камень не падают).</summary>
    public bool TileClear(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<GatherableComponent>(anchored))
                return false;

            if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }

    /// <summary>Тайл существует и свободен от породы и твёрдых объектов.</summary>
    public bool IsOpen(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        return !_map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty && TileClear(gridUid, grid, tile);
    }

    /// <summary>
    /// Умная проверка: от тайла по свободным соседям (4 стороны) можно дойти минимум до <paramref name="minTiles"/> тайлов.
    /// Так мобы и капсулы не падают в замурованную породой щель, из которой нет выхода.
    /// </summary>
    public bool IsOpenRegion(EntityUid gridUid, MapGridComponent grid, Vector2i start, int minTiles = 40)
    {
        if (!IsOpen(gridUid, grid, start))
            return false;

        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            if (seen.Count >= minTiles)
                return true;

            foreach (var next in new[] { tile + Vector2i.Up, tile + Vector2i.Down, tile + Vector2i.Left, tile + Vector2i.Right })
            {
                if (seen.Contains(next) || !IsOpen(gridUid, grid, next))
                    continue;

                seen.Add(next);
                queue.Enqueue(next);
            }
        }

        return seen.Count >= minTiles;
    }

    /// <summary>
    /// Находит место посадки в кольце вокруг точки: свободный тайл из открытой области. Кольцо понемногу расширяется.
    /// </summary>
    public EntityCoordinates? FindDropTile(EntityUid gridUid, Vector2 center, float minRing, float maxRing,
        Robust.Shared.Random.IRobustRandom random, int attempts = 80, int minRegion = 40)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var spread = attempt / 20f * 3f;
            var pos = center + random.NextAngle().ToVec() * random.NextFloat(minRing, maxRing + spread);
            var tile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, pos));

            if (!IsOpenRegion(gridUid, grid, tile, minRegion))
                continue;

            return _map.GridTileToLocal(gridUid, grid, tile);
        }

        return null;
    }
}
