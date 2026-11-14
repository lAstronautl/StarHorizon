using System.Numerics;
using Content.Server._Horizon.Planet;
using Content.Server.Procedural;
using Content.Server.Spawners.Components;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using Content.Shared._Horizon.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.Parallax;

// Horizon: постоянная зона планеты. Чанки в радиусе от центра генерируются заранее в фоне
// с лимитом времени на тик и больше не выгружаются; за радиусом биом грузится за игроком как раньше.
public sealed partial class BiomeSystem
{
    [Dependency] private DungeonSystem _dungeon = default!;

    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<BiomePreloadComponent> _preloadQuery;
    private float _unloadDelay = 30f;
    private float _unloadBudgetMs = 2f;

    /// <summary>
    /// По каждой карте: чанк → момент, когда он перестал быть нужен игрокам.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, TimeSpan>> _pendingUnload = new();
    private readonly List<Vector2i> _unloadScratch = new();
    private readonly Stopwatch _unloadStopwatch = new();
    private readonly HashSet<EntityUid> _preloadReady = new();
    private float _preloadBudgetMs = 3f;
    private readonly Stopwatch _preloadStopwatch = new();

    /// <summary>
    /// На сколько чанков очереди вперёд заранее строить рудные слои.
    /// </summary>
    private const int PreloadMarkerLookahead = 16;

    private void InitializeHorizonPreload()
    {
        _preloadQuery = GetEntityQuery<BiomePreloadComponent>();
        _metaQuery = GetEntityQuery<MetaDataComponent>();
        Subs.CVar(_configManager, HorizonCCVars.BiomeLoadTimeBudget, value => _loadBudgetMs = value, true);
        Subs.CVar(_configManager, HorizonCCVars.BiomePreloadTimeBudget, value => _preloadBudgetMs = value, true);
        Subs.CVar(_configManager, HorizonCCVars.BiomeUnloadDelay, value => _unloadDelay = value, true);
        Subs.CVar(_configManager, HorizonCCVars.BiomeUnloadTimeBudget, value => _unloadBudgetMs = value, true);
    }

    private void OnHorizonBiomeShutdown(EntityUid uid, BiomeComponent component, ComponentShutdown args)
    {
        _pendingUnload.Remove(uid);
    }

    private bool IsHorizonSpawner(EntityUid uid)
    {
        return HasComp<ConditionalSpawnerComponent>(uid)
               || HasComp<RandomSpawnerComponent>(uid)
               || HasComp<EntityTableSpawnerComponent>(uid)
               || HasComp<RoomFillComponent>(uid);
    }

    /// <summary>
    /// Во сколько раз радиус круга загрузки больше половины стороны прежнего квадрата.
    /// </summary>
    private const float LoadCircleFactor = 1.2f;

    /// <summary>
    /// Чанки ближе этого расстояния (в клетках) до зрителя грузятся сразу, без очереди.
    /// </summary>
    private const float NearLoadDistance = 24f + ChunkSize;

    private float _loadBudgetMs = 3f;
    private readonly Dictionary<BiomeComponent, List<Vector2>> _viewerPositions = new();
    private readonly List<(Vector2i Chunk, float Distance)> _deferredLoads = new();
    private readonly Stopwatch _loadStopwatch = new();
    private EntityQuery<MetaDataComponent> _metaQuery;

    private float HorizonViewerDistanceSquared(BiomeComponent component, Vector2i chunkOrigin)
    {
        if (!_viewerPositions.TryGetValue(component, out var viewers) || viewers.Count == 0)
            return 0f;

        var center = new Vector2(chunkOrigin.X + ChunkSize / 2f, chunkOrigin.Y + ChunkSize / 2f);
        var best = float.MaxValue;

        foreach (var viewer in viewers)
        {
            best = MathF.Min(best, Vector2.DistanceSquared(center, viewer));
        }

        return best;
    }

    /// <summary>
    /// Грузит отложенные дальние чанки, ближние первыми, пока не истёк лимит времени на тик.
    /// </summary>
    private void LoadDeferredChunks(BiomeComponent component, EntityUid gridUid, MapGridComponent grid, int seed)
    {
        if (_deferredLoads.Count == 0)
            return;

        _deferredLoads.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        _loadStopwatch.Restart();

        foreach (var (chunk, _) in _deferredLoads)
        {
            component.LoadedChunks.Add(chunk);
            LoadChunk(component, gridUid, grid, chunk, seed);

            if (_loadStopwatch.Elapsed.TotalMilliseconds >= _loadBudgetMs)
                break;
        }

        _deferredLoads.Clear();
    }

    /// <summary>
    /// Сущность не менялась с момента создания: тик последнего изменения не позже тика создания + 1.
    /// </summary>
    private bool IsHorizonUntouched(EntityUid uid)
    {
        return _metaQuery.TryGetComponent(uid, out var meta) &&
               meta.EntityLastModifiedTick.Value <= meta.CreationTick.Value + 1;
    }

    /// <summary>
    /// Выгружает чанки, которые не нужны игрокам дольше задержки, пока не истёк лимит времени на тик.
    /// Чанки постоянной зоны не выгружаются никогда.
    /// </summary>
    private void UnloadChunksHorizon(BiomeComponent component, EntityUid gridUid, MapGridComponent grid, int seed)
    {
        var active = _activeChunks[component];
        _preloadQuery.TryGetComponent(gridUid, out var preload);
        var pending = _pendingUnload.GetOrNew(gridUid);
        var now = _timing.CurTime;
        var delay = TimeSpan.FromSeconds(_unloadDelay);
        _unloadScratch.Clear();

        foreach (var chunk in component.LoadedChunks)
        {
            if (active.Contains(chunk))
            {
                if (pending.Count > 0)
                    pending.Remove(chunk);

                continue;
            }

            if (preload != null && IsHorizonPreloadChunk(preload, chunk))
                continue;

            if (!pending.TryGetValue(chunk, out var since))
            {
                since = now;
                pending[chunk] = now;
            }

            if (now - since >= delay)
                _unloadScratch.Add(chunk);
        }

        if (_unloadScratch.Count == 0)
            return;

        var tiles = new List<(Vector2i, Tile)>(ChunkSize * ChunkSize);
        _unloadStopwatch.Restart();

        foreach (var chunk in _unloadScratch)
        {
            component.LoadedChunks.Remove(chunk);
            pending.Remove(chunk);
            UnloadChunk(component, gridUid, grid, chunk, seed, tiles);

            if (_unloadStopwatch.Elapsed.TotalMilliseconds >= _unloadBudgetMs)
                break;
        }
    }

    /// <summary>
    /// Включает для карты с биомом постоянную зону указанного радиуса (в клетках).
    /// </summary>
    public void SetPreload(EntityUid mapUid, int radius, Vector2i? center = null)
    {
        if (radius <= 0)
            return;

        var comp = EnsureComp<BiomePreloadComponent>(mapUid);
        comp.Radius = radius;
        comp.Center = center ?? Vector2i.Zero;
        comp.Queue.Clear();
        comp.NextIndex = 0;
        comp.Completed = false;
        comp.Spent = TimeSpan.Zero;

        var min = SharedMapSystem.GetChunkIndices(comp.Center - new Vector2i(radius, radius), ChunkSize);
        var max = SharedMapSystem.GetChunkIndices(comp.Center + new Vector2i(radius, radius), ChunkSize);

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                var origin = new Vector2i(x, y) * ChunkSize;

                if (IsHorizonPreloadChunk(comp, origin))
                    comp.Queue.Add(origin);
            }
        }

        var c = comp.Center;
        comp.Queue.Sort((a, b) => ChunkDistanceSquared(a, c).CompareTo(ChunkDistanceSquared(b, c)));
    }

    private static float ChunkDistanceSquared(Vector2i chunkOrigin, Vector2i center)
    {
        var half = ChunkSize / 2f;
        var dx = chunkOrigin.X + half - center.X;
        var dy = chunkOrigin.Y + half - center.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Входит ли чанк (по его началу) в постоянную зону.
    /// </summary>
    private static bool IsHorizonPreloadChunk(BiomePreloadComponent preload, Vector2i chunkOrigin)
    {
        return ChunkDistanceSquared(chunkOrigin, preload.Center) <= (float) preload.Radius * preload.Radius;
    }

    /// <summary>
    /// Ставит в очередь на этот тик рудные чанки, покрывающие ближайшую порцию прогрузки,
    /// чтобы руда была посчитана до загрузки самих чанков.
    /// </summary>
    private void QueueHorizonPreloadMarkers()
    {
        _preloadReady.Clear();
        var query = AllEntityQuery<BiomePreloadComponent, BiomeComponent>();

        while (query.MoveNext(out var uid, out var preload, out var biome))
        {
            if (preload.Completed || !biome.Enabled || biome.LifeStage < ComponentLifeStage.Running)
                continue;

            // Пока на карте строится данж, зону не трогаем, иначе скалы окажутся внутри комнат.
            if (_dungeon.IsGenerating(uid))
                continue;

            _preloadReady.Add(uid);

            if (biome.MarkerLayers.Count == 0)
                continue;

            var end = Math.Min(preload.Queue.Count, preload.NextIndex + PreloadMarkerLookahead);

            for (var i = preload.NextIndex; i < end; i++)
            {
                var origin = preload.Queue[i];
                // Чуть меньше чанка, чтобы не захватывать соседние рудные чанки по границе.
                var area = new Box2(origin.X + 0.1f, origin.Y + 0.1f, origin.X + ChunkSize - 0.1f, origin.Y + ChunkSize - 0.1f);
                Preload(uid, biome, area);
            }
        }
    }

    /// <summary>
    /// Загружает следующие чанки постоянной зоны, пока не истёк лимит времени на тик.
    /// </summary>
    private void UpdateHorizonPreload(BiomeComponent component, EntityUid gridUid, MapGridComponent grid, int seed)
    {
        if (!_preloadReady.Contains(gridUid) || !_preloadQuery.TryGetComponent(gridUid, out var preload) || preload.Completed)
            return;

        _preloadStopwatch.Restart();
        // Рудные слои готовы только для ближайшей порции очереди.
        var end = Math.Min(preload.Queue.Count, preload.NextIndex + PreloadMarkerLookahead);

        while (preload.NextIndex < end)
        {
            var chunk = preload.Queue[preload.NextIndex];

            // Руда считается в фоне: ждём, пока районы, накрывающие чанк, будут готовы.
            if (!AreMarkersReady(component, chunk))
                break;

            preload.NextIndex++;

            LoadChunkMarkers(component, gridUid, grid, chunk, seed);

            if (component.LoadedChunks.Add(chunk))
                LoadChunk(component, gridUid, grid, chunk, seed);

            if (_preloadStopwatch.Elapsed.TotalMilliseconds >= _preloadBudgetMs)
                break;
        }

        preload.Spent += _preloadStopwatch.Elapsed;

        if (preload.NextIndex < preload.Queue.Count)
            return;

        preload.Completed = true;

        var entities = 0;
        foreach (var chunk in preload.Queue)
        {
            if (component.LoadedEntities.TryGetValue(chunk, out var ents))
                entities += ents.Count;
        }

        Log.Info($"Biome preload finished for {ToPrettyString(gridUid)}: radius {preload.Radius}, {preload.Queue.Count} chunks, {entities} biome entities, {preload.Spent.TotalSeconds:F1} s of tick time.");
    }
}
