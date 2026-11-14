using System.Collections.Concurrent;
using System.Threading.Tasks;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Content.Shared.Parallax.Biomes.Markers;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

// Horizon: расчёт рудных слоёв (районы 128×128) вынесен из игрового тика в фоновые задачи.
// Фоновая часть работает только с шумом и прототипами и не трогает сетку и сущности;
// результат вливается в компонент в главном потоке.
public sealed partial class BiomeSystem
{
    private readonly record struct MarkerJobKey(EntityUid Map, string Layer, Vector2i Chunk);

    private sealed record MarkerJobResult(
        MarkerJobKey Key,
        BiomeComponent Component,
        Dictionary<Vector2i, List<Vector2i>>? Pending,
        Exception? Error);

    private readonly HashSet<MarkerJobKey> _markerJobs = new();
    private readonly ConcurrentQueue<MarkerJobResult> _markerResults = new();

    private readonly List<(Vector2i Node, string? Prototype)> _markerSpawns = new();
    private readonly List<EntityUid> _markerReplaced = new();

    private void BuildMarkerChunksHorizon(BiomeComponent component, EntityUid gridUid, MapGridComponent grid, int seed)
    {
        ApplyMarkerResults();

        var markers = _markerChunks[component];
        var loadedMarkers = component.LoadedMarkers;
        var maxJobs = Math.Max(1, _parallel.ParallelProcessCount);
        List<IBiomeLayer>? layerSnapshot = null;
        var idx = 0;

        foreach (var (layer, chunks) in markers)
        {
            // I know dictionary ordering isn't guaranteed but I just need something to differentiate seeds.
            idx++;
            var localIdx = idx;
            var forced = component.ForcedMarkerLayers.Contains(layer);
            loadedMarkers.TryGetValue(layer, out var built);

            foreach (var chunk in chunks)
            {
                if (built != null && built.Contains(chunk))
                    continue;

                if (forced)
                {
                    BuildMarkerChunkSync(component, gridUid, grid, seed, layer, localIdx, chunk);
                    continue;
                }

                var key = new MarkerJobKey(gridUid, layer, chunk);

                if (_markerJobs.Contains(key) || _markerJobs.Count >= maxJobs)
                    continue;

                _markerJobs.Add(key);
                layerSnapshot ??= new List<IBiomeLayer>(component.Layers);
                var layers = layerSnapshot;
                var layerProto = ProtoManager.Index<BiomeMarkerLayerPrototype>(layer);
                var markerSeed = seed + chunk.X * ChunkSize + chunk.Y + localIdx;

                Task.Run(() =>
                {
                    try
                    {
                        var pending = ComputeMarkerChunk(layers, seed, layerProto, chunk, markerSeed);
                        _markerResults.Enqueue(new MarkerJobResult(key, component, pending, null));
                    }
                    catch (Exception e)
                    {
                        _markerResults.Enqueue(new MarkerJobResult(key, component, null, e));
                    }
                });
            }
        }

        component.ForcedMarkerLayers.Clear();
    }

    /// <summary>
    /// Вливает готовые результаты фоновых расчётов в компоненты. Только главный поток.
    /// </summary>
    private void ApplyMarkerResults()
    {
        while (_markerResults.TryDequeue(out var result))
        {
            _markerJobs.Remove(result.Key);
            var component = result.Component;

            if (component.Deleted || TerminatingOrDeleted(result.Key.Map))
                continue;

            if (result.Error != null)
                Log.Error($"Biome marker layer {result.Key.Layer} failed for chunk {result.Key.Chunk}: {result.Error}");

            var built = component.LoadedMarkers.GetOrNew(result.Key.Layer);

            // Район уже посчитан другим путём (например принудительным слоем).
            if (!built.Add(result.Key.Chunk) || result.Pending == null)
                continue;

            foreach (var (chunkOrigin, nodes) in result.Pending)
            {
                component.PendingMarkers.GetOrNew(chunkOrigin)[result.Key.Layer] = nodes;
            }

            // Чанки, которые уже загружены, получают руду сразу: иначе она ждала бы,
            // пока игрок снова окажется рядом.
            if (!TryComp<MapGridComponent>(result.Key.Map, out var grid))
                continue;

            foreach (var chunkOrigin in result.Pending.Keys)
            {
                if (component.LoadedChunks.Contains(chunkOrigin))
                    LoadChunkMarkers(component, result.Key.Map, grid, chunkOrigin, component.Seed);
            }
        }
    }

    /// <summary>
    /// Посчитаны ли все рудные районы, накрывающие чанк.
    /// </summary>
    private bool AreMarkersReady(BiomeComponent component, Vector2i chunkOrigin)
    {
        foreach (var layer in component.MarkerLayers)
        {
            var proto = ProtoManager.Index(layer);
            var markerChunk = SharedMapSystem.GetChunkIndices(chunkOrigin, proto.Size) * proto.Size;

            if (!component.LoadedMarkers.TryGetValue(layer, out var built) || !built.Contains(markerChunk))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Чистый расчёт клеток рудного слоя в районе. Выполняется в фоновом потоке:
    /// только шум и прототипы, без сетки и сущностей.
    /// </summary>
    private Dictionary<Vector2i, List<Vector2i>> ComputeMarkerChunk(
        List<IBiomeLayer> layers,
        int seed,
        BiomeMarkerLayerPrototype layerProto,
        Vector2i chunk,
        int markerSeed)
    {
        var rand = new Random(markerSeed);
        var buffer = (int) (layerProto.Radius / 2f);
        var bounds = new Box2i(chunk + buffer, chunk + layerProto.Size - buffer);
        var count = (int) (bounds.Area / (layerProto.Radius * layerProto.Radius));
        count = Math.Min(count, layerProto.MaxCount);

        var remainingTiles = new HashSet<Vector2i>();

        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            for (var y = bounds.Bottom; y < bounds.Top; y++)
            {
                var node = new Vector2i(x, y);
                string? proto = null;

                if (TryGetTile(node, layers, seed, (Entity<MapGridComponent>?) null, out var tile))
                    TryGetEntity(node, layers, tile.Value, seed, (Entity<MapGridComponent>?) null, out proto);

                // If there's an existing entity and it doesn't match the mask then skip.
                if (layerProto.EntityMask.Count > 0 &&
                    (proto == null || !layerProto.EntityMask.ContainsKey(proto)))
                {
                    continue;
                }

                // If it's just a flat spawn then just check for anything blocking.
                if (proto != null && layerProto.Prototype != null)
                    continue;

                remainingTiles.Add(node);
            }
        }

        var pending = new Dictionary<Vector2i, List<Vector2i>>();
        var frontier = new ValueList<Vector2i>(32);

        for (var i = 0; i < count; i++)
        {
            var groupSize = rand.Next(layerProto.MinGroupSize, layerProto.MaxGroupSize + 1);

            while (groupSize > 0 && remainingTiles.Count > 0)
            {
                var startNode = rand.PickAndTake(remainingTiles);
                frontier.Clear();
                frontier.Add(startNode);

                while (frontier.Count > 0 && groupSize > 0)
                {
                    var frontierIndex = rand.Next(frontier.Count);
                    var node = frontier[frontierIndex];
                    frontier.RemoveSwap(frontierIndex);
                    remainingTiles.Remove(node);

                    for (var x = -1; x <= 1; x++)
                    {
                        for (var y = -1; y <= 1; y++)
                        {
                            var neighbor = new Vector2i(node.X + x, node.Y + y);

                            if (frontier.Contains(neighbor) || !remainingTiles.Contains(neighbor))
                                continue;

                            frontier.Add(neighbor);
                        }
                    }

                    var chunkOrigin = SharedMapSystem.GetChunkIndices(node, ChunkSize) * ChunkSize;
                    pending.GetOrNew(chunkOrigin).Add(node);
                    groupSize--;
                }
            }
        }

        return pending;
    }
}
