using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.SpaceHazards;

/// <summary>
/// A planet in the sector map with exactly one expedition. The expedition map is generated only when a drop pod is
/// dropped on the planet, runs for the normal expedition time for everybody, and a new task is rolled when it closes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExpeditionPlanetComponent : Component
{
    /// <summary>Salvage biome mod id (Caves, Grasslands, Snow, Lava, Shadow...). Also selects the planet's look.</summary>
    [DataField, AutoNetworkedField]
    public string BiomeId = "Grasslands";

    [DataField, AutoNetworkedField]
    public float SpriteRadius = 40f;

    [DataField, AutoNetworkedField]
    public float Seed;

    /// <summary>Resolution of the pixel-art planet (pixels across the diameter).</summary>
    [DataField, AutoNetworkedField]
    public float Pixels = 160f;

    /// <summary>No worldgen debris is placed (and existing debris is removed) within this distance of the planet.</summary>
    [DataField]
    public float DebrisClearance = 2000f;

    /// <summary>Distance from the planet center within which a console can start an expedition.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 450f;

    /// <summary>
    /// The planet's single expedition task. Chosen randomly when first needed and re-rolled as soon as the
    /// expedition on this planet closes. Server-side only.
    /// </summary>
    [ViewVariables]
    public SalvageMissionParams? Mission;

    /// <summary>The expedition map, once a drop pod has been dropped on this planet. Server-side only.</summary>
    [ViewVariables]
    public EntityUid? ExpeditionMap;

    /// <summary>When the running expedition closes. Server-side only.</summary>
    [ViewVariables]
    public TimeSpan EndTime;

    /// <summary>
    /// Idle: the map does not exist (it is only generated when a pod is dropped). Generating: the map is being built.
    /// Active: the expedition is running for everybody. Server-side only.
    /// </summary>
    [ViewVariables]
    public PlanetExpeditionStatus Status = PlanetExpeditionStatus.Idle;
}
