using System.Numerics;
using Content.Client.Parallax;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Client.GameObjects;
using Robust.Client.Utility;
using Robust.Client.Graphics;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Color = Robust.Shared.Maths.Color;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Horizon.Shuttles;

/// <summary>
/// Draws a far-away top-down "camera" of the planet around the shuttle as the background of the shuttle console
/// MAP tab, in place of the parallax. Terrain comes from the biome (computed on the client, so it also shows the
/// parts that are not loaded yet) with real tiles taking priority; structures and items are drawn from the loaded
/// entities. Living creatures are left out on purpose.
/// </summary>
public sealed class PlanetMapCamera
{
    private const float TargetCellPixels = 5f;
    private const int MaxCells = 12000;
    private const int MaxEntities = 1200;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly Color Backdrop = new(0.02f, 0.02f, 0.035f);

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly BiomeSystem _biome;
    private readonly SharedMapSystem _maps;
    private readonly SharedTransformSystem _xform;
    private readonly EntityLookupSystem _lookup;

    private readonly Dictionary<int, Color> _tileColors = new();
    private readonly Dictionary<Vector2i, int?> _sampled = new();
    private readonly HashSet<Entity<SpriteComponent>> _entityScratch = new();
    private TimeSpan _cacheExpires;
    private MapId _cachedMap = MapId.Nullspace;

    public PlanetMapCamera()
    {
        IoCManager.InjectDependencies(this);
        var sys = _entMan.EntitySysManager;
        _biome = sys.GetEntitySystem<BiomeSystem>();
        _maps = sys.GetEntitySystem<SharedMapSystem>();
        _xform = sys.GetEntitySystem<SharedTransformSystem>();
        _lookup = sys.GetEntitySystem<EntityLookupSystem>();
    }

    /// <summary>True when <paramref name="mapId"/> is a planet the shuttle is on and the camera was drawn.</summary>
    public bool TryDraw(
        DrawingHandleScreen handle,
        MapId mapId,
        EntityUid? shuttle,
        Box2 viewBox,
        Func<Vector2, Vector2> toScreen,
        float pixelsPerMeter,
        UIBox2 bounds)
    {
        if (shuttle == null
            || mapId == MapId.Nullspace
            || !_entMan.TryGetComponent(shuttle.Value, out TransformComponent? shuttleXform)
            || shuttleXform.MapID != mapId)
        {
            return false;
        }

        var mapUid = _maps.GetMapOrInvalid(mapId);
        if (!_entMan.TryGetComponent(mapUid, out BiomeComponent? biome)
            || !_entMan.TryGetComponent(mapUid, out MapGridComponent? grid)
            || biome.Layers.Count == 0)
        {
            return false;
        }

        if (mapId != _cachedMap || _timing.RealTime > _cacheExpires)
        {
            _sampled.Clear();
            _cachedMap = mapId;
            _cacheExpires = _timing.RealTime + CacheLifetime;
        }

        handle.DrawRect(bounds, Backdrop);

        DrawTerrain(handle, (mapUid, grid), biome, viewBox, toScreen, pixelsPerMeter);
        DrawEntities(handle, mapId, viewBox, toScreen, pixelsPerMeter);
        return true;
    }

    private void DrawTerrain(
        DrawingHandleScreen handle,
        Entity<MapGridComponent> grid,
        BiomeComponent biome,
        Box2 viewBox,
        Func<Vector2, Vector2> toScreen,
        float pixelsPerMeter)
    {
        // One cell is a block of tiles sampled at its center; blocks are aligned to a fixed lattice so panning
        // reuses cached samples.
        var cell = Math.Max(1, (int) MathF.Ceiling(TargetCellPixels / MathF.Max(pixelsPerMeter, 0.0001f)));

        var minX = (int) MathF.Floor(viewBox.Left / cell) * cell;
        var maxX = (int) MathF.Ceiling(viewBox.Right / cell) * cell;
        var minY = (int) MathF.Floor(viewBox.Bottom / cell) * cell;
        var maxY = (int) MathF.Ceiling(viewBox.Top / cell) * cell;

        while (((maxX - minX) / cell + 1) * ((maxY - minY) / cell + 1) > MaxCells)
        {
            cell *= 2;
            minX = (int) MathF.Floor(viewBox.Left / cell) * cell;
            maxX = (int) MathF.Ceiling(viewBox.Right / cell) * cell;
            minY = (int) MathF.Floor(viewBox.Bottom / cell) * cell;
            maxY = (int) MathF.Ceiling(viewBox.Top / cell) * cell;
        }

        for (var x = minX; x < maxX; x += cell)
        {
            for (var y = minY; y < maxY; y += cell)
            {
                var sample = new Vector2i(x + cell / 2, y + cell / 2);
                if (!TryGetTileId(sample, grid, biome, out var tileId))
                    continue;

                var a = toScreen(new Vector2(x, y));
                var b = toScreen(new Vector2(x + cell, y + cell));
                var box = new UIBox2(
                    MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y),
                    MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

                handle.DrawRect(box, GetTileColor(tileId));
            }
        }
    }

    private bool TryGetTileId(Vector2i indices, Entity<MapGridComponent> grid, BiomeComponent biome, out int tileId)
    {
        if (!_sampled.TryGetValue(indices, out var cached))
        {
            Entity<MapGridComponent>? gridArg = grid;
            cached = _biome.TryGetBiomeTile(indices, biome.Layers, biome.Seed, gridArg, out var tile) && !tile.Value.IsEmpty
                ? tile.Value.TypeId
                : null;
            _sampled[indices] = cached;
        }

        tileId = cached ?? 0;
        return cached != null;
    }

    private Color GetTileColor(int tileId)
    {
        if (_tileColors.TryGetValue(tileId, out var color))
            return color;

        color = AverageTileColor(tileId);
        _tileColors[tileId] = color;
        return color;
    }

    private Color AverageTileColor(int tileId)
    {
        var fallback = Color.FromHsv(new Vector4((tileId * 0.137f) % 1f, 0.25f, 0.45f, 1f));

        try
        {
            if (_tileDefs[tileId] is not ContentTileDefinition { Sprite: { } sprite })
                return fallback;

            using var stream = _resources.ContentFileRead(sprite);
            using var image = Image.Load<Rgba32>(stream);

            double r = 0, g = 0, b = 0;
            var count = 0;
            foreach (var p in image.GetPixelSpan())
            {
                if (p.A < 128)
                    continue;

                r += p.R;
                g += p.G;
                b += p.B;
                count++;
            }

            return count == 0
                ? fallback
                : new Color((float) (r / count / 255.0), (float) (g / count / 255.0), (float) (b / count / 255.0));
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void DrawEntities(
        DrawingHandleScreen handle,
        MapId mapId,
        Box2 viewBox,
        Func<Vector2, Vector2> toScreen,
        float pixelsPerMeter)
    {
        // Sprites are authored at 32 px per meter; scale them so that one meter is `pixelsPerMeter` map pixels.
        var scale = new Vector2(pixelsPerMeter / 32f);

        _entityScratch.Clear();
        _lookup.GetEntitiesIntersecting(mapId, viewBox, _entityScratch, LookupFlags.Static | LookupFlags.Dynamic | LookupFlags.Sundries);

        var drawn = 0;
        foreach (var (uid, sprite) in _entityScratch)
        {
            if (drawn >= MaxEntities)
                break;

            if (!sprite.Visible
                || _entMan.HasComponent<MobStateComponent>(uid)
                || _entMan.HasComponent<GhostComponent>(uid)
                || !_entMan.TryGetComponent(uid, out TransformComponent? xform)
                || xform.ParentUid != xform.GridUid && xform.ParentUid != xform.MapUid)
            {
                continue;
            }

            var pos = toScreen(_xform.GetWorldPosition(xform));
            handle.DrawEntity(uid, pos, scale, -_xform.GetWorldRotation(xform), Angle.Zero, Direction.South, sprite);
            drawn++;
        }
    }
}
