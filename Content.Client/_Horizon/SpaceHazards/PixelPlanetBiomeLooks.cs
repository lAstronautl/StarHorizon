// LuaCorp - This file is licensed under AGPLv3
// Copyright (c) 2026 LuaCorp Contributors
// See AGPLv3.txt for details.

using System.Numerics;

namespace Content.Client._Horizon.SpaceHazards;

/// <summary>
/// Parameters for the <c>PixelBiomePlanet</c> shader: float uniforms (noise scale, color thresholds thr1..thr9,
/// overlay cover, ...) and color uniforms (color0..9 terrain ramp, ov_c0..2 overlay).
/// </summary>
public readonly record struct PixelPlanetLook(
    IReadOnlyDictionary<string, float> Floats,
    IReadOnlyDictionary<string, Vector3> Colors);

/// <summary>
/// Biome planet presets for UI previews (expedition mission cards). Each one was fitted to the pre-rendered
/// expedition planet sprite of that biome: terrain colors are placed by thresholds so their pixel shares match
/// the sprite, and noise scale/octaves/dither were tuned so edge density, blob count and blob size match too.
/// </summary>
public static class PixelPlanetBiomeLooks
{
    public const string ShaderId = "PixelBiomePlanet";

    private static readonly Dictionary<string, PixelPlanetLook> Presets = CreatePresets();

    private static Dictionary<string, PixelPlanetLook> CreatePresets()
    {
        var presets = new Dictionary<string, PixelPlanetLook>
        {
            ["Grasslands"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.4499f,
                ["detail_size"] = 9.1173f,
                ["dither_size"] = 0.5898f,
                ["light_strength"] = 0.1062f,
                ["octaves"] = 5.4224f,
                ["ov_band"] = 0.0002f,
                ["ov_cover"] = 0.6012f,
                ["ov_enabled"] = 1f,
                ["ov_light_border"] = 0.1935f,
                ["ov_octaves"] = 1.6344f,
                ["ov_size"] = 9.1719f,
                ["ov_stretch"] = 3.0509f,
                ["size"] = 6.3726f,
                ["thr1"] = 0.335f,
                ["thr2"] = 0.4384f,
                ["thr3"] = 0.4504f,
                ["thr4"] = 0.5388f,
                ["thr5"] = 99f,
                ["thr6"] = 99f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 0f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#283540"),
                ["color1"] = H("#4fa4b8"),
                ["color2"] = H("#1b832d"),
                ["color3"] = H("#38733b"),
                ["color4"] = H("#63ab3f"),
                ["color5"] = H("#63ab3f"),
                ["color6"] = H("#63ab3f"),
                ["color7"] = H("#63ab3f"),
                ["color8"] = H("#63ab3f"),
                ["color9"] = H("#63ab3f"),
                ["ov_c0"] = H("#dfe0e8"),
                ["ov_c1"] = H("#686f99"),
                ["ov_c2"] = H("#404973"),
            }),
            ["Lava"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.0101f,
                ["detail_size"] = 15.2903f,
                ["dither_size"] = 0f,
                ["light_strength"] = 0.0803f,
                ["octaves"] = 5.8974f,
                ["ov_cover"] = 99f,
                ["ov_enabled"] = 0f,
                ["size"] = 2.6805f,
                ["thr1"] = 0.4489f,
                ["thr2"] = 0.4911f,
                ["thr3"] = 0.5188f,
                ["thr4"] = 0.5599f,
                ["thr5"] = 0.5919f,
                ["thr6"] = 99f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 0.5808f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#3d2936"),
                ["color1"] = H("#52333f"),
                ["color2"] = H("#8f4d57"),
                ["color3"] = H("#ad2f45"),
                ["color4"] = H("#e64539"),
                ["color5"] = H("#ff8933"),
                ["color6"] = H("#ff8933"),
                ["color7"] = H("#ff8933"),
                ["color8"] = H("#ff8933"),
                ["color9"] = H("#ff8933"),
                ["ov_c0"] = H("#000000"),
                ["ov_c1"] = H("#000000"),
                ["ov_c2"] = H("#000000"),
            }),
            ["Snow"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.0437f,
                ["detail_size"] = 8.5838f,
                ["dither_size"] = 0.127f,
                ["light_strength"] = 0.5055f,
                ["octaves"] = 3.8133f,
                ["ov_cover"] = 99f,
                ["ov_enabled"] = 0f,
                ["size"] = 4.5113f,
                ["thr1"] = 0.3597f,
                ["thr2"] = 0.4289f,
                ["thr3"] = 0.4538f,
                ["thr4"] = 0.5128f,
                ["thr5"] = 0.5356f,
                ["thr6"] = 0.5889f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 0.5168f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#8dafc7"),
                ["color1"] = H("#bfbfbd"),
                ["color2"] = H("#bfc3e6"),
                ["color3"] = H("#aed1dd"),
                ["color4"] = H("#a4d2f4"),
                ["color5"] = H("#d8dcd7"),
                ["color6"] = H("#dfe7f2"),
                ["color7"] = H("#dfe7f2"),
                ["color8"] = H("#dfe7f2"),
                ["color9"] = H("#dfe7f2"),
                ["ov_c0"] = H("#000000"),
                ["ov_c1"] = H("#000000"),
                ["ov_c2"] = H("#000000"),
            }),
            ["LowDesert"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.3955f,
                ["detail_size"] = 7.6474f,
                ["dither_size"] = 0.9458f,
                ["light_strength"] = 0.0294f,
                ["octaves"] = 4.3821f,
                ["ov_band"] = 0.0283f,
                ["ov_cover"] = 0.5253f,
                ["ov_enabled"] = 1f,
                ["ov_light_border"] = 0.6245f,
                ["ov_octaves"] = 3.9049f,
                ["ov_size"] = 4.5447f,
                ["ov_stretch"] = 2.0692f,
                ["size"] = 5.8013f,
                ["thr1"] = 0.312f,
                ["thr2"] = 0.354f,
                ["thr3"] = 0.3658f,
                ["thr4"] = 0.427f,
                ["thr5"] = 0.4415f,
                ["thr6"] = 0.5245f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 0.7717f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#6d75a1"),
                ["color1"] = H("#91a3b9"),
                ["color2"] = H("#4e4723"),
                ["color3"] = H("#9c5541"),
                ["color4"] = H("#a16c48"),
                ["color5"] = H("#947c60"),
                ["color6"] = H("#ceb37d"),
                ["color7"] = H("#ceb37d"),
                ["color8"] = H("#ceb37d"),
                ["color9"] = H("#ceb37d"),
                ["ov_c0"] = H("#dae8e7"),
                ["ov_c1"] = H("#b6cad0"),
                ["ov_c2"] = H("#908b8e"),
            }),
            ["Caves"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.0299f,
                ["detail_size"] = 13.3543f,
                ["dither_size"] = 0f,
                ["light_strength"] = 0.1085f,
                ["octaves"] = 2.8193f,
                ["ov_cover"] = 99f,
                ["ov_enabled"] = 0f,
                ["size"] = 7.4967f,
                ["thr1"] = 0.2497f,
                ["thr2"] = 0.2971f,
                ["thr3"] = 0.4174f,
                ["thr4"] = 0.5185f,
                ["thr5"] = 0.5384f,
                ["thr6"] = 99f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 1.6085f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#2b2338"),
                ["color1"] = H("#2f2f2e"),
                ["color2"] = H("#5f5e5c"),
                ["color3"] = H("#818079"),
                ["color4"] = H("#8e8b8b"),
                ["color5"] = H("#bdb9b9"),
                ["color6"] = H("#bdb9b9"),
                ["color7"] = H("#bdb9b9"),
                ["color8"] = H("#bdb9b9"),
                ["color9"] = H("#bdb9b9"),
                ["ov_c0"] = H("#000000"),
                ["ov_c1"] = H("#000000"),
                ["ov_c2"] = H("#000000"),
            }),
            ["Shadow"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.2917f,
                ["detail_size"] = 6.8664f,
                ["dither_size"] = 0f,
                ["light_strength"] = 0.295f,
                ["octaves"] = 4.7613f,
                ["ov_cover"] = 99f,
                ["ov_enabled"] = 0f,
                ["size"] = 5.5163f,
                ["thr1"] = 0.4632f,
                ["thr2"] = 0.5423f,
                ["thr3"] = 0.5975f,
                ["thr4"] = 0.6152f,
                ["thr5"] = 0.6385f,
                ["thr6"] = 0.6672f,
                ["thr7"] = 0.692f,
                ["thr8"] = 0.7126f,
                ["thr9"] = 0.7416f,
                ["warp"] = 2.8743f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#0f091b"),
                ["color1"] = H("#17101d"),
                ["color2"] = H("#191a23"),
                ["color3"] = H("#1b1a1f"),
                ["color4"] = H("#220f25"),
                ["color5"] = H("#27191f"),
                ["color6"] = H("#2a1a26"),
                ["color7"] = H("#2b0b3b"),
                ["color8"] = H("#3e1a31"),
                ["color9"] = H("#3f245f"),
                ["ov_c0"] = H("#000000"),
                ["ov_c1"] = H("#000000"),
                ["ov_c2"] = H("#000000"),
            }),
            ["GateJungle"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0f,
                ["detail_size"] = 12.0983f,
                ["dither_size"] = 0.5322f,
                ["light_strength"] = 0.0132f,
                ["octaves"] = 2f,
                ["ov_cover"] = 99f,
                ["ov_enabled"] = 0f,
                ["size"] = 7.4559f,
                ["thr1"] = 0.2035f,
                ["thr2"] = 0.3567f,
                ["thr3"] = 0.3614f,
                ["thr4"] = 0.3896f,
                ["thr5"] = 0.417f,
                ["thr6"] = 99f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 1.8248f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#0a2810"),
                ["color1"] = H("#103818"),
                ["color2"] = H("#0c3020"),
                ["color3"] = H("#184830"),
                ["color4"] = H("#1a4a1c"),
                ["color5"] = H("#2a6a28"),
                ["color6"] = H("#2a6a28"),
                ["color7"] = H("#2a6a28"),
                ["color8"] = H("#2a6a28"),
                ["color9"] = H("#2a6a28"),
                ["ov_c0"] = H("#000000"),
                ["ov_c1"] = H("#000000"),
                ["ov_c2"] = H("#000000"),
            }),
            ["GateInferno"] = new(
            new Dictionary<string, float>
            {
                ["detail_mix"] = 0.6413f,
                ["detail_size"] = 5.9868f,
                ["dither_size"] = 0.2391f,
                ["light_strength"] = 0.5188f,
                ["octaves"] = 1.5866f,
                ["ov_band"] = 0.0475f,
                ["ov_cover"] = 0.6834f,
                ["ov_enabled"] = 1f,
                ["ov_light_border"] = 0.7006f,
                ["ov_octaves"] = 3.3894f,
                ["ov_size"] = 10.7213f,
                ["ov_stretch"] = 3.2705f,
                ["size"] = 3.6506f,
                ["thr1"] = 0.3374f,
                ["thr2"] = 0.4746f,
                ["thr3"] = 0.5008f,
                ["thr4"] = 0.6184f,
                ["thr5"] = 99f,
                ["thr6"] = 99f,
                ["thr7"] = 99f,
                ["thr8"] = 99f,
                ["thr9"] = 99f,
                ["warp"] = 2.8755f,
            },
            new Dictionary<string, Vector3>
            {
                ["color0"] = H("#1a0202"),
                ["color1"] = H("#2a0404"),
                ["color2"] = H("#3a0606"),
                ["color3"] = H("#4a0808"),
                ["color4"] = H("#6a1010"),
                ["color5"] = H("#6a1010"),
                ["color6"] = H("#6a1010"),
                ["color7"] = H("#6a1010"),
                ["color8"] = H("#6a1010"),
                ["color9"] = H("#6a1010"),
                ["ov_c0"] = H("#ff6010"),
                ["ov_c1"] = H("#ffb020"),
                ["ov_c2"] = H("#ff1808"),
            }),
        };
        presets["Lavalight"] = presets["Lava"];
        return presets;
    }

    private static readonly (string Token, string Preset)[] Tokens =
    [
        ("inferno", "GateInferno"),
        ("lava", "Lava"),
        ("volcan", "Lava"),
        ("snow", "Snow"),
        ("ice", "Snow"),
        ("desert", "LowDesert"),
        ("wasteland", "LowDesert"),
        ("savanna", "LowDesert"),
        ("shadow", "Shadow"),
        ("cave", "Caves"),
        ("jungle", "GateJungle"),
        ("bioluminescent", "GateJungle"),
    ];

    /// <summary>Resolves a biome/expedition id to a look. Unknown ids fall back to Grasslands.</summary>
    public static PixelPlanetLook Resolve(string? biomeId)
    {
        if (biomeId != null)
        {
            if (Presets.TryGetValue(biomeId, out var exact))
                return exact;

            // "GateLavaCaves" etc: the first matching token decides.
            var lower = biomeId.ToLowerInvariant();
            foreach (var (token, preset) in Tokens)
            {
                if (lower.Contains(token))
                    return Presets[preset];
            }
        }

        return Presets["Grasslands"];
    }

    private static Vector3 H(string hex)
    {
        var c = Color.FromHex(hex);
        return new Vector3(c.R, c.G, c.B);
    }
}
