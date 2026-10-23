using System.Numerics;
using Content.Client._Horizon.SpaceHazards;
using Content.Shared._Horizon.SpaceHazards;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.Shuttles;

/// <summary>
/// Icons for the shuttle console MAP tab: the planets themselves (drawn with the same pixel-planet shaders as in
/// the world) and the threats around them (black holes, stars), each with a label.
/// </summary>
public sealed class SectorMapIcons
{
    private const float MinIconPixels = 22f;
    private const float MaxIconPixels = 72f;
    private const float MapPixels = 48f;

    private static readonly Color BlackHoleColor = Color.FromHex("#A040FF");
    private static readonly Color BlackHoleHorizon = Color.FromHex("#F5F540");
    private static readonly Color StarColor = Color.FromHex("#FFB020");
    private static readonly Color LabelShadow = Color.Black.WithAlpha(0.8f);

    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

    private readonly Dictionary<EntityUid, ShaderInstance> _shaders = new();
    private readonly Font _font;

    public SectorMapIcons()
    {
        IoCManager.InjectDependencies(this);
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 9);
    }

    public void Draw(
        DrawingHandleScreen handle,
        IEntityManager entMan,
        SharedTransformSystem xformSys,
        MapId mapId,
        Func<Vector2, Vector2> toScreen,
        float pixelsPerMeter,
        UIBox2 bounds)
    {
        if (mapId == MapId.Nullspace)
            return;

        var time = (float) _timing.RealTime.TotalSeconds;

        // Planets people can launch expeditions from: biome look + their own name.
        var expedition = entMan.AllEntityQueryEnumerator<ExpeditionPlanetComponent, TransformComponent, MetaDataComponent>();
        while (expedition.MoveNext(out var uid, out var planet, out var xform, out var meta))
        {
            if (xform.MapID != mapId)
                continue;

            var center = toScreen(xformSys.GetWorldPosition(xform));
            var size = IconSize(planet.SpriteRadius, pixelsPerMeter);
            if (!Visible(center, size, bounds))
                continue;

            var look = PixelPlanetBiomeLooks.Resolve(planet.BiomeId);
            var shader = GetShader(uid, look.Kind);
            PixelPlanetBiomeLooks.Apply(shader, look, time + planet.Seed, MapPixels, planet.Seed);
            DrawShaderIcon(handle, shader, center, size);
            // The name is drawn by the beacon marker of the planet, so no label here.
        }

        // Decorative far-away planets.
        var background = entMan.AllEntityQueryEnumerator<SectorBackgroundPlanetComponent, TransformComponent>();
        while (background.MoveNext(out var uid, out var planet, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var center = toScreen(xformSys.GetWorldPosition(xform));
            var size = IconSize(planet.SpriteRadius, pixelsPerMeter);
            if (!Visible(center, size, bounds))
                continue;

            var shader = GetShader(uid, planet.PlanetKind);
            PixelPlanetShaderSetup.Configure(shader, planet.PlanetKind, time + planet.Seed, MapPixels, planet.Seed,
                planet.Rotation, new Vector2(planet.LightOriginX, planet.LightOriginY));
            PixelPlanetPalettes.ApplyPlanet(shader, planet.PlanetKind, planet.PaletteIndex);
            DrawShaderIcon(handle, shader, center, size);
            DrawLabel(handle, Loc.GetString("sector-map-planet"), center, size, Color.FromHex("#C5CEDB"));
        }

        // Threats: black holes and stars.
        var bodies = entMan.AllEntityQueryEnumerator<SectorCelestialBodyComponent, TransformComponent>();
        while (bodies.MoveNext(out _, out var body, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var center = toScreen(xformSys.GetWorldPosition(xform));
            var size = IconSize(body.SpriteRadius, pixelsPerMeter);
            if (!Visible(center, size, bounds))
                continue;

            var radius = size / 2f;
            if (body.Kind == CelestialKind.BlackHole)
            {
                handle.DrawCircle(center, radius, Color.Black);
                handle.DrawCircle(center, radius, BlackHoleColor, filled: false);
                handle.DrawCircle(center, radius * 0.55f, BlackHoleHorizon.WithAlpha(0.85f), filled: false);
                DrawLabel(handle, Loc.GetString("sector-map-black-hole"), center, size, BlackHoleColor);
            }
            else
            {
                handle.DrawCircle(center, radius, StarColor.WithAlpha(0.9f));
                handle.DrawCircle(center, radius, Color.White.WithAlpha(0.6f), filled: false);
                var label = body.Harmless
                    ? Loc.GetString("sector-map-star-safe", ("multiplier", body.SolarBoostMultiplier.ToString("0.#")))
                    : Loc.GetString("sector-map-star");
                DrawLabel(handle, label, center, size, StarColor);
            }
        }
    }

    /// <summary>Called when the control is disposed, to free the per-planet shaders.</summary>
    public void Dispose()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();

        _shaders.Clear();
    }

    private ShaderInstance GetShader(EntityUid uid, PixelPlanetKind kind)
    {
        if (_shaders.TryGetValue(uid, out var shader))
            return shader;

        shader = _protos.Index(PixelPlanetShaderSetup.ShaderId(kind)).InstanceUnique();
        _shaders[uid] = shader;
        return shader;
    }

    private static float IconSize(float spriteRadiusMeters, float pixelsPerMeter)
    {
        return Math.Clamp(spriteRadiusMeters * 2f * pixelsPerMeter, MinIconPixels, MaxIconPixels);
    }

    private static bool Visible(Vector2 center, float size, UIBox2 bounds)
    {
        return center.X > bounds.Left - size && center.X < bounds.Right + size
               && center.Y > bounds.Top - size && center.Y < bounds.Bottom + size;
    }

    private static void DrawShaderIcon(DrawingHandleScreen handle, ShaderInstance shader, Vector2 center, float size)
    {
        handle.UseShader(shader);
        handle.DrawTextureRect(Texture.White, UIBox2.FromDimensions(center - new Vector2(size / 2f), new Vector2(size)));
        handle.UseShader(null);
    }

    private void DrawLabel(DrawingHandleScreen handle, string text, Vector2 center, float iconSize, Color color)
    {
        var dims = handle.GetDimensions(_font, text, 1f);
        var pos = center + new Vector2(-dims.X / 2f, iconSize / 2f + 2f);
        handle.DrawString(_font, pos + new Vector2(1f, 1f), text, LabelShadow);
        handle.DrawString(_font, pos, text, color);
    }
}
