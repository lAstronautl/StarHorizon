using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.SpaceHazards;

/// <summary>
/// Worldgen controller: scatters expedition planets (one look per biome) around the sector map.
/// </summary>
[RegisterComponent]
public sealed partial class ExpeditionPlanetPlacerControllerComponent : Component
{
    [DataField]
    public List<string> Biomes = ["Caves", "Grasslands", "Snow", "Lava", "Shadow"];

    /// <summary>How many planets to place for every biome.</summary>
    [DataField]
    public int CountPerBiome = 1;

    [DataField]
    public float MinDistance = 1500f;

    [DataField]
    public float MaxDistance = 6500f;

    /// <summary>Minimum distance between two planets, so the 450 unit ranges never overlap.</summary>
    [DataField]
    public float MinSeparation = 1200f;

    [DataField]
    public EntProtoId PlanetPrototype = "ExpeditionPlanet";

    [DataField]
    public bool Spawned;
}
