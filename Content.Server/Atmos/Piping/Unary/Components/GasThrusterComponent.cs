using Content.Server.Atmos.Piping.Unary.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Guidebook;

namespace Content.Server.Atmos.Piping.Unary.Components;

/// <summary>
/// Turns a <see cref="Content.Server.Shuttles.Components.ThrusterComponent"/> into a gas-powered engine: the
/// thrust it produces scales with the pressure of the gas piped into it, and firing the thruster vents that
/// gas out into space.
/// </summary>
[RegisterComponent]
[Access(typeof(GasThrusterSystem))]
public sealed partial class GasThrusterComponent : Component
{
    [DataField("inlet")]
    public string InletName = "pipe";

    /// <summary>
    /// Pipe pressure, in kPa, below which the engine produces no thrust at all.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MinPressure = Atmospherics.OneAtmosphere * 0.2f;

    /// <summary>
    /// Pipe pressure, in kPa, at or above which the engine produces its maximum thrust.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MaxPressure = Atmospherics.OneAtmosphere * 5f;

    /// <summary>
    /// Thrust produced once the pipe pressure reaches <see cref="MaxPressure"/>.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MaxThrust = 100f;

    /// <summary>
    /// Fraction of the inlet pipe's gas volume vented per second while firing at maximum thrust. Scales down
    /// linearly with the current thrust ratio, so a barely-firing engine sips gas rather than dumping it.
    /// </summary>
    [DataField]
    public float MaxTransferRate = 20f;
}
