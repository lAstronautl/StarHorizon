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
    [Dependency] private readonly Robust.Shared.Map.IMapManager _mapManager = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly Content.Shared.Parallax.Biomes.SharedBiomeSystem _biome = default!;

    /// <summary>
    /// Тайл пола данжа: на нём лежит настоящий тайл, и он не тот, что сгенерировал бы биом планеты (данж кладёт свой пол).
    /// Без биома (не планета) любой тайл считается полом.
    /// </summary>
    public bool IsDungeonFloor(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(gridUid, grid, tile);
        if (tileRef.Tile.IsEmpty)
            return false;

        if (!TryComp<Content.Shared.Parallax.Biomes.BiomeComponent>(gridUid, out var biome))
            return true;

        if (!_biome.TryGetTile(tile, biome.Layers, biome.Seed, (gridUid, grid), out var natural))
            return true;

        return natural.Value.TypeId != tileRef.Tile.TypeId;
    }

    /// <summary>
    /// Место посадки на полу данжа в кольце вокруг точки: свободный тайл пола данжа из открытой области (комната или
    /// коридор, а не замурованная щель). Проверяются все тайлы кольца, выбирается случайный подходящий.
    /// </summary>
    public EntityCoordinates? FindDungeonDropTile(EntityUid gridUid, Vector2 center, float minRing, float maxRing,
        Robust.Shared.Random.IRobustRandom random, int minRegion = 12, int maxChecks = 40)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var centerTile = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, center));
        var radius = (int) MathF.Ceiling(maxRing);
        var candidates = new List<Vector2i>();
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < minRing || dist > maxRing)
                    continue;

                var tile = new Vector2i(centerTile.X + dx, centerTile.Y + dy);
                if (IsDungeonFloor(gridUid, grid, tile) && TileClear(gridUid, grid, tile))
                    candidates.Add(tile);
            }
        }

        random.Shuffle(candidates);
        for (var i = 0; i < candidates.Count && i < maxChecks; i++)
        {
            if (IsOpenRegion(gridUid, grid, candidates[i], minRegion))
                return _map.GridTileToLocal(gridUid, grid, candidates[i]);
        }

        return null;
    }

    /// <summary>
    /// Опасный пол: пропасть или лава/жидкая плазма (эффект клетки поджигает и т.п.; вода только тушит, она не опасна).
    /// Сюда капсулы не падают и мобы погони не идут.
    /// </summary>
    public bool IsHazard(EntityUid anchored)
    {
        if (HasComp<Content.Shared.Chasm.ChasmComponent>(anchored))
            return true;

        return TryComp<Content.Server.Tiles.TileEntityEffectComponent>(anchored, out var effect)
               && effect.Effects.Any(e => e is not Content.Shared.EntityEffects.Effects.ExtinguishReaction);
    }

    /// <summary>На тайле нет ни породы, ни других твёрдых объектов, ни лавы с пропастью: сюда можно садиться.</summary>
    public bool TileClear(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<GatherableComponent>(anchored) || IsHazard(anchored))
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

    /// <summary>Клетка проходима: свободна, или единственные твёрдые объекты на ней двери (если двери открываются).</summary>
    public bool IsPassable(EntityUid gridUid, MapGridComponent grid, Vector2i tile, bool passDoors)
    {
        if (_map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty)
            return false;

        foreach (var anchored in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (HasComp<GatherableComponent>(anchored) || IsHazard(anchored))
                return false;

            if (TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard
                && !(passDoors && HasComp<Content.Shared.Doors.Components.DoorComponent>(anchored)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Клетки планеты под другими гридами (севшими шаттлами), для мест падения капсул: <c>Covered</c> все, <c>Blocked</c>
    /// только под их стенами и прочими препятствиями (двери проходимы, если <paramref name="passDoors"/>).
    /// </summary>
    public (HashSet<Vector2i> Covered, HashSet<Vector2i> Blocked) OtherGrids(EntityUid gridUid, MapGridComponent grid, Vector2 center,
        float radius, bool passDoors)
    {
        var blocked = new HashSet<Vector2i>();
        var covered = new HashSet<Vector2i>();
        var mapId = Transform(gridUid).MapID;
        var box = new Box2(center - new Vector2(radius, radius), center + new Vector2(radius, radius));
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(mapId, box, ref grids);

        foreach (var other in grids)
        {
            if (other.Owner == gridUid)
                continue;

            var matrix = _xform.GetWorldMatrix(other.Owner);
            foreach (var tileRef in _map.GetAllTiles(other.Owner, other.Comp))
            {
                var isBlocked = !IsPassable(other.Owner, other.Comp, tileRef.GridIndices, passDoors);

                // Клетка другого грида может лечь на две клетки планеты (сдвиг, поворот): закрываем все, что задевает.
                var local = _map.TileCenterToVector((other.Owner, other.Comp), tileRef.GridIndices);
                foreach (var corner in new[] { new Vector2(-0.4f, -0.4f), new Vector2(0.4f, -0.4f), new Vector2(-0.4f, 0.4f), new Vector2(0.4f, 0.4f), Vector2.Zero })
                {
                    var world = Vector2.Transform(local + corner, matrix);
                    var under = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, world));
                    covered.Add(under);
                    if (isBlocked)
                        blocked.Add(under);
                }
            }
        }

        return (covered, blocked);
    }

    /// <summary>
    /// Поле расстояний: сколько шагов (4 стороны) от <paramref name="start"/> до каждой проходимой клетки, не больше
    /// <paramref name="maxTiles"/> клеток. По нему мобы идут к игроку «под горку»: такой путь всегда реальный.
    /// </summary>
    public Dictionary<Vector2i, int> DistanceField(EntityUid gridUid, MapGridComponent grid, Vector2i start, bool passDoors,
        int maxTiles = 20000)
    {
        var dist = new Dictionary<Vector2i, int> { [start] = 0 };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.Count > 0 && dist.Count < maxTiles)
        {
            var tile = queue.Dequeue();
            var steps = dist[tile] + 1;
            foreach (var next in new[] { tile + Vector2i.Up, tile + Vector2i.Down, tile + Vector2i.Left, tile + Vector2i.Right })
            {
                if (dist.ContainsKey(next) || !IsPassable(gridUid, grid, next, passDoors))
                    continue;

                dist[next] = steps;
                queue.Enqueue(next);
            }
        }

        return dist;
    }

    /// <summary>
    /// Клетки, до которых можно дойти от <paramref name="start"/> по свободным тайлам (4 стороны, без породы, стен и
    /// закрытых дверей), с числом шагов. <paramref name="within"/>: только эти клетки (например, клетки комплекса).
    /// </summary>
    public List<(Vector2i Tile, int Steps)> Reachable(EntityUid gridUid, MapGridComponent grid, Vector2i start, int maxSteps,
        IReadOnlySet<Vector2i>? within = null, int maxTiles = 4000, IReadOnlySet<Vector2i>? blocked = null)
    {
        var result = new List<(Vector2i, int)> { (start, 0) };
        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<(Vector2i, int)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0 && seen.Count < maxTiles)
        {
            var (tile, steps) = queue.Dequeue();
            if (steps >= maxSteps)
                continue;

            foreach (var next in new[] { tile + Vector2i.Up, tile + Vector2i.Down, tile + Vector2i.Left, tile + Vector2i.Right })
            {
                if (seen.Contains(next) || within != null && !within.Contains(next) || blocked != null && blocked.Contains(next)
                    || !IsOpen(gridUid, grid, next))
                    continue;

                seen.Add(next);
                result.Add((next, steps + 1));
                queue.Enqueue((next, steps + 1));
            }
        }

        return result;
    }

    /// <summary>Ближайший к точке свободный тайл из открытой области (центр тайла в координатах карты-грида), если есть.</summary>
    public Vector2? FindOpenNear(EntityUid gridUid, Vector2 point, int radius = 3, int minRegion = 12)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var center = _map.TileIndicesFor(gridUid, grid, new EntityCoordinates(gridUid, point));
        Vector2i? best = null;
        var bestDist = float.MaxValue;
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var d = dx * dx + dy * dy;
                if (d >= bestDist)
                    continue;

                var tile = new Vector2i(center.X + dx, center.Y + dy);
                if (!IsOpenRegion(gridUid, grid, tile, minRegion))
                    continue;

                best = tile;
                bestDist = d;
            }
        }

        return best is { } found ? _map.GridTileToLocal(gridUid, grid, found).Position : null;
    }
}
