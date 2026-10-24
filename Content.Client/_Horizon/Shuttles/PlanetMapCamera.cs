using System.Numerics;
using Content.Client.Parallax;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
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
/// parts that are not loaded yet). Only terrain is drawn: structures, items and creatures stay hidden so the
/// expedition's complex is not given away.
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
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly BiomeSystem _biome;
    private readonly SharedMapSystem _maps;
    private readonly SharedTransformSystem _xform;
    private readonly EntityLookupSystem _lookup;
    private readonly SpriteSystem _sprites;

    private readonly Dictionary<int, Color> _tileColors = new();
    private readonly Dictionary<Vector2i, (int? Tile, string? Relief)> _sampled = new();
    private readonly Dictionary<int, Texture?> _tileTextures = new();
    private readonly Dictionary<string, Texture?> _reliefTextures = new();
    private readonly Dictionary<string, Color> _reliefColors = new();
    private readonly HashSet<Entity<SpriteComponent>> _entityScratch = new();
    private readonly Dictionary<int, int> _tileCounts = new();
    private int? _dominantTile;
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
        _sprites = sys.GetEntitySystem<SpriteSystem>();
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

        // Cells without a tile (not generated / chasms) take the planet's dominant ground colour instead of black.
        handle.DrawRect(bounds, _dominantTile is { } dominant ? GetTileColor(dominant) : Backdrop);

        DrawTerrain(handle, (mapUid, grid), biome, viewBox, toScreen, pixelsPerMeter);
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

        _tileCounts.Clear();
        for (var x = minX; x < maxX; x += cell)
        {
            for (var y = minY; y < maxY; y += cell)
            {
                var sample = new Vector2i(x + cell / 2, y + cell / 2);
                if (!TryGetTileId(sample, grid, biome, out var tileId, out var relief))
                    continue;

                var a = toScreen(new Vector2(x, y));
                var b = toScreen(new Vector2(x + cell, y + cell));
                var box = new UIBox2(
                    MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y),
                    MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

                // Real ground texture, with the biome's own rocks, crystals and plants drawn on top.
                if (GetTileTexture(tileId) is { } groundTexture)
                    handle.DrawTextureRect(groundTexture, box);
                else
                    handle.DrawRect(box, GetTileColor(tileId));

                if (relief != null)
                {
                    if (GetReliefTexture(relief) is { } reliefTexture)
                        handle.DrawTextureRect(reliefTexture, box);
                    else
                        handle.DrawRect(box, GetReliefColor(relief));
                }
                _tileCounts[tileId] = _tileCounts.GetValueOrDefault(tileId) + 1;
            }
        }

        var best = -1;
        foreach (var (id, count) in _tileCounts)
        {
            if (best < 0 || count > _tileCounts[best])
                best = id;
        }

        if (best >= 0)
            _dominantTile = best;
    }

    private bool TryGetTileId(Vector2i indices, Entity<MapGridComponent> grid, BiomeComponent biome, out int tileId, out string? relief)
    {
        if (!_sampled.TryGetValue(indices, out var cached))
        {
            Entity<MapGridComponent>? gridArg = grid;
            int? tileType = null;
            string? reliefId = null;

            if (_biome.TryGetBiomeTile(indices, biome.Layers, biome.Seed, gridArg, out var tile) && !tile.Value.IsEmpty)
            {
                tileType = tile.Value.TypeId;

                // Rocks, crystals and plants of the biome are what gives the terrain its relief.
                if (_biome.TryGetEntity(indices, biome.Layers, tile.Value, biome.Seed, gridArg, out var entityId))
                    reliefId = entityId;
            }

            cached = (tileType, reliefId);
            _sampled[indices] = cached;
        }

        tileId = cached.Tile ?? 0;
        relief = cached.Relief;
        return cached.Tile != null;
    }

    private Texture? GetTileTexture(int tileId)
    {
        if (_tileTextures.TryGetValue(tileId, out var cached))
            return cached;

        Texture? texture = null;
        try
        {
            if (_tileDefs[tileId] is ContentTileDefinition { Sprite: { } sprite })
            {
                texture = _cache.GetResource<TextureResource>(sprite).Texture;

                // Tiles with variants keep them side by side in one strip: use the first one.
                if (texture.Width > texture.Height)
                    texture = new AtlasTexture(texture, UIBox2.FromDimensions(0f, 0f, texture.Height, texture.Height));
            }
        }
        catch (Exception)
        {
            texture = null;
        }

        _tileTextures[tileId] = texture;
        return texture;
    }

    private Texture? GetReliefTexture(string entityId)
    {
        if (_reliefTextures.TryGetValue(entityId, out var cached))
            return cached;

        Texture? texture = null;
        try
        {
            texture = _sprites.GetPrototypeIcon(entityId).Default;
        }
        catch (Exception)
        {
            texture = null;
        }

        _reliefTextures[entityId] = texture;
        return texture;
    }

    private Color GetReliefColor(string entityId)
    {
        if (_reliefColors.TryGetValue(entityId, out var color))
            return color;

        var id = entityId.ToLowerInvariant();
        if (id.Contains("crystal") || id.Contains("ore") || id.Contains("gem"))
            color = new Color(0.45f, 0.78f, 0.85f);
        else if (id.Contains("flora") || id.Contains("tree") || id.Contains("grass") || id.Contains("bush") || id.Contains("plant"))
            color = new Color(0.18f, 0.42f, 0.2f);
        else if (id.Contains("snow") || id.Contains("ice"))
            color = new Color(0.85f, 0.9f, 0.95f);
        else if (id.Contains("wall") || id.Contains("rock") || id.Contains("stalag") || id.Contains("asteroid"))
            color = new Color(0.22f, 0.2f, 0.19f);
        else
            color = Color.FromHsv(new Vector4((entityId.GetHashCode() & 0xFF) / 255f, 0.25f, 0.4f, 1f));

        _reliefColors[entityId] = color;
        return color;
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
}
