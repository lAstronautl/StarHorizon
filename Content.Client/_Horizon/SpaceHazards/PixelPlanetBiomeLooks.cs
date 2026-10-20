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
public readonly record struct PixelPlanetLook(PixelPlanetKind Kind, Vector3[] Palette);

public static class PixelPlanetBiomeLooks
{
    // Slot layout per kind is documented on PixelPlanetPalettes.GetPalette: LandMasses = 3 water, 4 land, 3 cloud;
    // NoAtmosphere = 3 base, 2 crater; LavaWorld = 3 base, 2 crater, 3 lava; IceWorld = 3 base, 3 lake, 4 cloud.
    private static readonly Dictionary<string, PixelPlanetLook> Presets = new()
    {
        ["Grasslands"] = new(PixelPlanetKind.LandMasses, PixelPlanetPalettes.GetPalette(PixelPlanetKind.LandMasses, 0)),
        ["Lava"] = new(PixelPlanetKind.LavaWorld, PixelPlanetPalettes.GetPalette(PixelPlanetKind.LavaWorld, 0)),
        ["Lavalight"] = new(PixelPlanetKind.LavaWorld, PixelPlanetPalettes.GetPalette(PixelPlanetKind.LavaWorld, 0)),
        ["Snow"] = new(PixelPlanetKind.IceWorld, PixelPlanetPalettes.GetPalette(PixelPlanetKind.IceWorld, 0)),
        ["LowDesert"] = new(PixelPlanetKind.LandMasses, new[]
        {
        H("#b6cad0"),
        H("#91a3b9"),
        H("#6d75a1"),
        H("#ceb37d"),
        H("#947c60"),
        H("#a16c48"),
        H("#9c5541"),
        H("#dae8e7"),
        H("#b6cad0"),
        H("#908b8e")
        }),
        ["Caves"] = new(PixelPlanetKind.NoAtmosphere, new[]
        {
        H("#bdb9b9"),
        H("#818079"),
        H("#5f5e5c"),
        H("#8e8b8b"),
        H("#2f2f2e")
        }),
        ["Shadow"] = new(PixelPlanetKind.NoAtmosphere, new[]
        {
        H("#2a1a26"),
        H("#191a23"),
        H("#0f091b"),
        H("#27191f"),
        H("#17101d")
        }),
        ["GateJungle"] = new(PixelPlanetKind.LandMasses, new[]
        {
        H("#184830"),
        H("#103818"),
        H("#0a2810"),
        H("#2a6a28"),
        H("#1a4a1c"),
        H("#103818"),
        H("#0c3020"),
        H("#1a4a1c"),
        H("#184830"),
        H("#0c3020")
        }),
        ["GateInferno"] = new(PixelPlanetKind.LavaWorld, new[]
        {
        H("#6a1010"),
        H("#4a0808"),
        H("#3a0606"),
        H("#2a0404"),
        H("#1a0202"),
        H("#ffb020"),
        H("#ff6010"),
        H("#ff1808")
        }),
    };

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
