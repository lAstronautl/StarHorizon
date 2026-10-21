using Content.Shared.Damage;

namespace Content.Server._Horizon.SpaceHazards;

/// <summary>
/// Marks the hyperspace map used for singularity jumps. Shuttles travelling through it keep taking hull damage
/// from the singularity, and the map has its own parallax (see SectorCelestialBodyComponent.TransitParallax).
/// </summary>
[RegisterComponent]
public sealed partial class SingularityTransitMapComponent : Component
{
    /// <summary>Hull damage applied to every shuttle in transit at each tick.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>Exterior tiles hit per shuttle per tick.</summary>
    [DataField]
    public int Hits = 8;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    public TimeSpan NextDamage;
}
