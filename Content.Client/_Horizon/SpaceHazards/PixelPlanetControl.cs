// LuaCorp - This file is licensed under AGPLv3
// Copyright (c) 2026 LuaCorp Contributors
// See AGPLv3.txt for details.

using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.SpaceHazards;

/// <summary>
/// Small rotating pixel planet for UI (e.g. expedition mission cards), drawn with the PixelBiomePlanet shader
/// instead of a pre-rendered 361-frame sprite sheet. Use <see cref="SetBiome"/> to pick the look.
/// </summary>
public sealed class PixelPlanetControl : Control
{
    public const int NativePixels = 48;
    public const float DisplayScale = 1.5f;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly ShaderInstance _shader;
    private readonly float _seed;
    private PixelPlanetLook _look = PixelPlanetBiomeLooks.Resolve(null);

    public PixelPlanetControl()
    {
        IoCManager.InjectDependencies(this);

        _shader = _protoMan.Index<ShaderPrototype>(PixelPlanetBiomeLooks.ShaderId).InstanceUnique();
        _seed = 0.01f + _random.NextFloat() * 9.99f;
        var size = NativePixels * DisplayScale;
        MinSize = new Vector2(size, size);
        SetSize = new Vector2(size, size);
        HorizontalAlignment = HAlignment.Center;
        ApplyLook();
    }

    public void SetBiome(string? biomeId)
    {
        _look = PixelPlanetBiomeLooks.Resolve(biomeId);
        ApplyLook();
    }

    private void ApplyLook()
    {
        _shader.SetParameter("pixels", (float) NativePixels);
        _shader.SetParameter("seed", _seed);
        _shader.SetParameter("rotation", 0f);
        _shader.SetParameter("time_speed", 0.02f);
        _shader.SetParameter("light_origin", new Vector2(0.39f, 0.39f));
        _shader.SetParameter("ov_seed", _seed + 1.5f);
        _shader.SetParameter("ov_speed", 0.015f);
        _shader.SetParameter("debug_mode", 0f);

        foreach (var (name, value) in _look.Floats)
            _shader.SetParameter(name, value);

        foreach (var (name, value) in _look.Colors)
            _shader.SetParameter(name, value);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!VisibleInTree)
            return;

        _shader.SetParameter("time", (float) _timing.RealTime.TotalSeconds + _seed);

        var size = MathF.Min(PixelSize.X, PixelSize.Y);
        var square = new Vector2(size, size);
        var origin = (PixelSize - square) / 2f;

        handle.UseShader(_shader);
        handle.DrawTextureRect(Texture.White, UIBox2.FromDimensions(origin, square));
        handle.UseShader(null);
    }
}
