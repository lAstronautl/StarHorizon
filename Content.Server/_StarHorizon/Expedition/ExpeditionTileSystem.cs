using System.Linq;
using System.Numerics;
using Content.Server.Gatherable.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Клетки карт экспедиций: какие свободны (без породы, твёрдых объектов, лавы и пропастей), до каких можно дойти,
/// поле расстояний для погони мобов волн и места падения капсул (волн, приёмника, игроков).
/// </summary>
public sealed class ExpeditionTileSystem : EntitySystem
{
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

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

    /// <summary>
    /// Клетка для капсулы, от которой до точки <paramref name="near"/> (игрока) гарантированно есть путь: поиск в ширину по
    /// свободным клеткам (без стен, породы и закрытых дверей), 5..14 шагов. Если игрок в комплексе, только клетки
    /// комплекса. Маркеры мест падения (<see cref="ExpeditionDropSiteMarkerComponent"/>) из атласов в первую очередь.
    /// </summary>
    public EntityCoordinates? FindReachableDropTile(EntityUid map, Vector2 near, HashSet<Vector2i>? taken = null,
        int minSteps = 5, int maxSteps = 14)
    {
        if (!TryComp<MapGridComponent>(map, out var grid))
            return null;

        var start = _map.TileIndicesFor(map, grid, new EntityCoordinates(map, near));
        IReadOnlySet<Vector2i>? within = null;
        if (TryComp<ExpeditionDungeonTilesComponent>(map, out var dungeon) && dungeon.Tiles.Contains(start))
            within = dungeon.Tiles;

        // Севшие шаттлы (другие гриды): их стены для поиска стены (шлюзы проходимы), а на сам шаттл капсула не падает,
        // только возле него.
        var (covered, blocked) = OtherGrids(map, grid, near, maxSteps + 20, passDoors: true);
        blocked.Remove(start);

        var reach = Reachable(map, grid, start, maxSteps + 6, within, blocked: blocked);
        if (within != null && reach.Count < 8)
            reach = Reachable(map, grid, start, maxSteps + 6, blocked: blocked); // тесная комната: допускаем и снаружи

        // Игрок на шаттле: от него до выхода с шаттла шагов больше, ищем дальше.
        if (covered.Contains(start))
            reach = Reachable(map, grid, start, maxSteps + 30, within, blocked: blocked);

        bool Free(Vector2i tile) => !covered.Contains(tile) && (taken == null || !taken.Contains(tile));

        // Маркеры из атласов: если до них можно дойти.
        var markerTiles = new HashSet<Vector2i>();
        var markers = EntityQueryEnumerator<ExpeditionDropSiteMarkerComponent, TransformComponent>();
        while (markers.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                markerTiles.Add(_map.TileIndicesFor(map, grid, xform.Coordinates));
        }

        var candidates = reach.Where(r => markerTiles.Contains(r.Tile) && r.Steps >= 2 && Free(r.Tile)).Select(r => r.Tile).ToList();
        if (candidates.Count == 0)
            candidates = reach.Where(r => r.Steps >= minSteps && r.Steps <= maxSteps && Free(r.Tile)).Select(r => r.Tile).ToList();
        if (candidates.Count == 0)
            candidates = reach.Where(r => r.Steps >= 2 && Free(r.Tile)).Select(r => r.Tile).ToList();
        if (candidates.Count == 0)
            return null;

        var pick = _random.Pick(candidates);
        taken?.Add(pick);
        return _map.GridTileToLocal(map, grid, pick);
    }

    /// <summary>
    /// Место посадки капсулы игроков у площадки (рядом с ней нет игроков, поэтому поиска от игрока нет): ближайшая
    /// к точке свободная клетка из области, в которой не меньше <paramref name="minRegion"/> клеток (не замурованная
    /// щель), и случайная клетка этой области не дальше <paramref name="radius"/> от точки.
    /// </summary>
    public EntityCoordinates? FindLandingTile(EntityUid map, Vector2 near, float radius = 6f, int minRegion = 25)
    {
        if (!TryComp<MapGridComponent>(map, out var grid))
            return null;

        var center = _map.TileIndicesFor(map, grid, new EntityCoordinates(map, near));
        var r = (int) MathF.Ceiling(radius);
        var starts = new List<Vector2i>();
        for (var dx = -r; dx <= r; dx++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy <= radius * radius)
                    starts.Add(new Vector2i(center.X + dx, center.Y + dy));
            }
        }

        starts.Sort((a, b) => (a - center).LengthSquared.CompareTo((b - center).LengthSquared));
        var tried = new HashSet<Vector2i>();
        foreach (var start in starts)
        {
            if (tried.Contains(start) || !IsOpen(map, grid, start))
                continue;

            var region = Reachable(map, grid, start, maxSteps: 2 * r + 10, maxTiles: Math.Max(400, minRegion));
            tried.UnionWith(region.Select(t => t.Tile));
            if (region.Count < minRegion)
                continue;

            var near2 = region.Where(t => (t.Tile - center).LengthSquared <= radius * radius).Select(t => t.Tile).ToList();
            var pick = near2.Count > 0 ? _random.Pick(near2) : start;
            return _map.GridTileToLocal(map, grid, pick);
        }

        return null;
    }
}
