// LuaWorld - This file is licensed under AGPLv3
// Copyright (c) 2026 LuaWorld Contributors
// See AGPLv3.txt for details.

using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.SpaceHazards;

public enum CelestialKind : byte
{
    Star = 0,
    BlackHole = 1,
}

/// <summary>
/// Visual-only distant star or black hole drawn by the pixel planet overlay.
/// Gameplay hazards (damage, pull, FTL exclusion) from the upstream port are intentionally not included.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SectorCelestialBodyComponent : Component
{
    [DataField, AutoNetworkedField]
    public CelestialKind Kind = CelestialKind.Star;

    [DataField, AutoNetworkedField]
    public float SpriteRadius = 48f;

    [DataField, AutoNetworkedField]
    public float Seed;

    [DataField, AutoNetworkedField]
    public float Pixels = 1024f;

    [DataField, AutoNetworkedField]
    public byte Palette;

    [DataField, AutoNetworkedField]
    public bool VisualsInitialized;
}
