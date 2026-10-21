// LuaCorp - This file is licensed under AGPLv3
// Copyright (c) 2026 LuaCorp Contributors
// See AGPLv3.txt for details.

using System.Numerics;
using Content.Shared._Horizon.SpaceHazards;

namespace Content.Client._Horizon.SpaceHazards;

/// <summary>
/// Shader + palette preset for a biome planet preview. Colors were taken from the pre-rendered
/// expedition planet sprites (Pixel Planets style), so the live shader matches them.
/// </summary>
/// <param name="Overrides">Shader float parameters tuned per look; applied after <see cref="PixelPlanetShaderSetup.Configure"/>.</param>
public readonly record struct PixelPlanetLook(
    PixelPlanetKind Kind,
    Vector3[] Palette,
    IReadOnlyDictionary<string, float> Overrides);

public static class PixelPlanetBiomeLooks
{
    // Slot layout per kind is documented on PixelPlanetPalettes.GetPalette: LandMasses = 3 water, 4 land, 3 cloud;
    // NoAtmosphere = 3 base, 2 crater; LavaWorld = 3 base, 2 crater, 3 lava; IceWorld = 3 base, 3 lake, 4 cloud.
    // Palettes and overrides were fitted so the live shader's color distribution matches the sprite of each biome.
    private static readonly Dictionary<string, PixelPlanetLook> Presets = CreatePresets();

    private static Dictionary<string, PixelPlanetLook> CreatePresets()
    {
        var presets = new Dictionary<string, PixelPlanetLook>
        {
            ["Grasslands"] = new(PixelPlanetKind.LandMasses,
            [
                H("#4fa4b8"),
                H("#4fa4b8"),
                H("#283540"),
                H("#63ab3f"),
                H("#1b832d"),
                H("#38733b"),
                H("#38733b"),
                H("#dfe0e8"),
                H("#686f99"),
                H("#404973"),
            ],
            new Dictionary<string, float>
            {
                ["cloud_cover"] = 0.705f,
                ["land_cutoff"] = 0.397f,
                ["land_light_border_1"] = 0.142f,
                ["land_light_border_2"] = 0.973f,
                ["light_border_1"] = 0.86f,
                ["light_border_2"] = 0.715f,
            }),
            ["Lava"] = new(PixelPlanetKind.LavaWorld,
            [
                H("#8f4d57"),
                H("#52333f"),
                H("#3d2936"),
                H("#52333f"),
                H("#3d2936"),
                H("#ff8933"),
                H("#e64539"),
                H("#ad2f45"),
            ],
            new Dictionary<string, float>
            {
                ["crater_light_border"] = 0.122f,
                ["lava_border_1"] = 0.093f,
                ["lava_border_2"] = 0.03f,
                ["lava_cutoff"] = 0.497f,
                ["lava_size"] = 4.589f,
                ["light_border_1"] = 0.442f,
                ["light_border_2"] = 0.542f,
            }),
            ["Snow"] = new(PixelPlanetKind.IceWorld,
            [
                H("#dfe7f2"),
                H("#d8dcd7"),
                H("#bfbfbd"),
                H("#aed1dd"),
                H("#a4d2f4"),
                H("#8dafc7"),
                H("#dfe7f2"),
                H("#bfc3e6"),
                H("#d8dcd7"),
                H("#bfc3e6"),
            ],
            new Dictionary<string, float>
            {
                ["cloud_cover"] = 0.492f,
                ["lake_cutoff"] = 0.413f,
                ["lake_size"] = 12.491f,
                ["light_border_1"] = 0.534f,
                ["light_border_2"] = 0.643f,
            }),
            ["LowDesert"] = new(PixelPlanetKind.LandMasses,
            [
                H("#91a3b9"),
                H("#6d75a1"),
                H("#6d75a1"),
                H("#ceb37d"),
                H("#947c60"),
                H("#9c5541"),
                H("#4e4723"),
                H("#dae8e7"),
                H("#b6cad0"),
                H("#908b8e"),
            ],
            new Dictionary<string, float>
            {
                ["cloud_cover"] = 0.429f,
                ["land_cutoff"] = 0.378f,
                ["land_light_border_1"] = 0.768f,
                ["land_light_border_2"] = 0.652f,
                ["light_border_1"] = 0.618f,
                ["light_border_2"] = 0.663f,
            }),
            ["Caves"] = new(PixelPlanetKind.NoAtmosphere,
            [
                H("#bdb9b9"),
                H("#818079"),
                H("#5f5e5c"),
                H("#5f5e5c"),
                H("#2f2f2e"),
            ],
            new Dictionary<string, float>
            {
                ["crater_light_border"] = 0.282f,
                ["crater_size"] = 8.269f,
                ["light_border_1"] = 0.378f,
                ["light_border_2"] = 0.54f,
            }),
            ["Shadow"] = new(PixelPlanetKind.NoAtmosphere,
            [
                H("#191a23"),
                H("#17101d"),
                H("#0f091b"),
                H("#17101d"),
                H("#0f091b"),
            ],
            new Dictionary<string, float>
            {
                ["crater_light_border"] = 0.257f,
                ["crater_size"] = 2.735f,
                ["light_border_1"] = 0.372f,
                ["light_border_2"] = 0.586f,
            }),
            ["GateJungle"] = new(PixelPlanetKind.LandMasses,
            [
                H("#103818"),
                H("#0c3020"),
                H("#0a2810"),
                H("#2a6a28"),
                H("#1a4a1c"),
                H("#184830"),
                H("#184830"),
                H("#1a4a1c"),
                H("#184830"),
                H("#0c3020"),
            ],
            new Dictionary<string, float>
            {
                ["cloud_cover"] = 3f,
                ["land_cutoff"] = 0.437f,
                ["land_light_border_1"] = 0.804f,
                ["land_light_border_2"] = 0.956f,
                ["light_border_1"] = 1.364f,
                ["light_border_2"] = 0.75f,
            }),
            ["GateInferno"] = new(PixelPlanetKind.LavaWorld,
            [
                H("#6a1010"),
                H("#4a0808"),
                H("#2a0404"),
                H("#3a0606"),
                H("#1a0202"),
                H("#ffb020"),
                H("#ff6010"),
                H("#ff1808"),
            ],
            new Dictionary<string, float>
            {
                ["crater_light_border"] = 0.89f,
                ["lava_border_1"] = 0.005f,
                ["lava_border_2"] = 0.161f,
                ["lava_cutoff"] = 0.847f,
                ["light_border_1"] = 0.38f,
                ["light_border_2"] = 0.525f,
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
