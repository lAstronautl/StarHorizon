using Content.Server._Horizon.Planet;
using Content.Shared._Horizon.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

// Horizon: скалы только на поверхности массива. Сущность, закрывающая обзор, не создаётся,
// если все 8 соседних клеток — такая же порода: живой игрок её не видит. Клетка запоминается
// как скрытая; когда соседнюю породу убирают (выкопали, взорвали, сел шаттл), скрытая достраивается.
public sealed partial class BiomeSystem
{
    private bool _hideInterior = true;

    /// <summary>
    /// Карта → начало чанка → скрытые клетки.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, HashSet<Vector2i>>> _hidden = new();

    private readonly Dictionary<string, bool> _occluderProtos = new();
    private readonly Dictionary<Vector2i, bool> _rockScratch = new();
    private readonly List<(EntityUid Map, Vector2i Tile)> _pendingReveals = new();
    private EntityQuery<OccluderComponent> _occluderQuery;

    /// <summary>
    /// Поднят, пока биом сам удаляет свои сущности (выгрузка чанка, замена рудой): достраивать соседей не нужно.
    /// </summary>
    private bool _biomeDeleting;

    private void InitializeHorizonSurface()
    {
        _occluderQuery = GetEntityQuery<OccluderComponent>();
        Subs.CVar(_configManager, HorizonCCVars.BiomeHideInteriorWalls, value => _hideInterior = value, true);
        SubscribeLocalEvent<BiomeSurfaceComponent, EntityTerminatingEvent>(OnSurfaceTerminating);
    }

    private void OnSurfaceTerminating(Entity<BiomeSurfaceComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_biomeDeleting || TerminatingOrDeleted(ent.Comp.Map))
            return;

        _pendingReveals.Add((ent.Comp.Map, ent.Comp.Tile));
    }

    private bool IsOccluderProto(string id)
    {
        if (_occluderProtos.TryGetValue(id, out var result))
            return result;

        result = _proto.TryIndex<EntityPrototype>(id, out var proto) && proto.Components.ContainsKey("Occluder");
        _occluderProtos[id] = result;
        return result;
    }

    private static Vector2i HorizonChunkOf(Vector2i tile)
    {
        return SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;
    }

    private bool IsHidden(EntityUid map, Vector2i tile)
    {
        return _hidden.TryGetValue(map, out var chunks) &&
               chunks.TryGetValue(HorizonChunkOf(tile), out var set) &&
               set.Contains(tile);
    }

    private bool RemoveHidden(EntityUid map, Vector2i tile)
    {
        return _hidden.TryGetValue(map, out var chunks) &&
               chunks.TryGetValue(HorizonChunkOf(tile), out var set) &&
               set.Remove(tile);
    }

    private void DropHiddenChunk(EntityUid map, Vector2i chunk)
    {
        if (_hidden.TryGetValue(map, out var chunks))
            chunks.Remove(chunk);
    }

    /// <summary>
    /// Можно ли не создавать породу на клетке: все 8 соседей — порода (стоящая, скрытая или та, что будет создана).
    /// </summary>
    private bool IsInteriorRock(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Vector2i loadingChunk,
        HashSet<Vector2i> loadingModified)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                if (!IsRockForHiding(component, gridUid, grid, new Vector2i(tile.X + x, tile.Y + y), loadingChunk, loadingModified))
                    return false;
            }
        }

        return true;
    }

    private bool IsRockForHiding(
        BiomeComponent component,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i tile,
        Vector2i loadingChunk,
        HashSet<Vector2i> loadingModified)
    {
        if (_rockScratch.TryGetValue(tile, out var cached))
            return cached;

        var result = Compute();
        _rockScratch[tile] = result;
        return result;

        bool Compute()
        {
            var chunk = HorizonChunkOf(tile);

            if (chunk == loadingChunk)
            {
                if (loadingModified.Contains(tile))
                    return false;
            }
            else if (component.ModifiedTiles.TryGetValue(chunk, out var modified) && modified.Contains(tile))
            {
                return false;
            }

            if (IsHidden(gridUid, tile))
                return true;

            var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
            var any = false;

            while (anchored.MoveNext(out var existing))
            {
                any = true;

                if (_occluderQuery.HasComp(existing.Value))
                    return true;
            }

            if (any)
                return false;

            // Чанк загружен раньше, а на клетке пусто и она не скрытая: породу там уже убрали.
            if (chunk != loadingChunk && component.LoadedChunks.Contains(chunk))
                return false;

            // Клетка ещё не загружена (или грузится прямо сейчас): решает биом.
            return TryGetEntity(tile, component, (gridUid, grid), out var proto) && IsOccluderProto(proto);
        }
    }

    /// <summary>
    /// Помечает сущность, если рядом с ней есть скрытая порода.
    /// </summary>
    private void MarkIfNearHidden(EntityUid gridUid, EntityUid ent, Vector2i tile)
    {
        if (!_hidden.TryGetValue(gridUid, out var chunks) || chunks.Count == 0)
            return;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                if (!IsHidden(gridUid, new Vector2i(tile.X + x, tile.Y + y)))
                    continue;

                var surface = EnsureComp<BiomeSurfaceComponent>(ent);
                surface.Map = gridUid;
                surface.Tile = tile;
                return;
            }
        }
    }

    /// <summary>
    /// После загрузки чанка: помечает всю породу, стоящую рядом с его скрытыми клетками.
    /// </summary>
    private void MarkAroundHidden(EntityUid gridUid, MapGridComponent grid, HashSet<Vector2i> hidden)
    {
        foreach (var cell in hidden)
        {
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0)
                        continue;

                    var tile = new Vector2i(cell.X + x, cell.Y + y);

                    if (hidden.Contains(tile))
                        continue;

                    var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);

                    while (anchored.MoveNext(out var existing))
                    {
                        var surface = EnsureComp<BiomeSurfaceComponent>(existing.Value);
                        surface.Map = gridUid;
                        surface.Tile = tile;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Клетка перестала быть породой: снять с неё скрытость и достроить скрытых соседей на следующем тике.
    /// </summary>
    private void QueueReveal(EntityUid gridUid, Vector2i tile)
    {
        if (!_hidden.TryGetValue(gridUid, out var chunks) || chunks.Count == 0)
            return;

        RemoveHidden(gridUid, tile);
        _pendingReveals.Add((gridUid, tile));
    }

    private void ProcessReveals()
    {
        if (_pendingReveals.Count == 0)
            return;

        // Список может пополняться во время обхода (достроенная порода сама ничего не раскрывает, но на всякий случай).
        for (var i = 0; i < _pendingReveals.Count; i++)
        {
            var (map, tile) = _pendingReveals[i];

            if (TerminatingOrDeleted(map) ||
                !_biomeQuery.TryGetComponent(map, out var biome) ||
                !TryComp<MapGridComponent>(map, out var grid))
            {
                continue;
            }

            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0)
                        continue;

                    var neighbor = new Vector2i(tile.X + x, tile.Y + y);

                    if (!RemoveHidden(map, neighbor))
                        continue;

                    RevealTile(biome, map, grid, neighbor);
                }
            }
        }

        _pendingReveals.Clear();
    }

    private void RevealTile(BiomeComponent biome, EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);

        if (anchored.MoveNext(out _) || !TryGetEntity(tile, biome, (gridUid, grid), out var proto))
            return;

        var ent = Spawn(proto, _mapSystem.GridTileToLocal(gridUid, grid, tile));

        if (_xformQuery.TryGetComponent(ent, out var xform) && !xform.Anchored)
            _transform.AnchorEntity((ent, xform), (gridUid, grid), tile);

        if (biome.LoadedEntities.TryGetValue(HorizonChunkOf(tile), out var loaded))
            loaded[ent] = tile;

        MarkIfNearHidden(gridUid, ent, tile);
    }
}
