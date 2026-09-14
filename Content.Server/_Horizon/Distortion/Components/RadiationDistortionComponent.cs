namespace Content.Server._Horizon.Distortion.Components;

/// <summary>
/// Tracks a decaying radiation exposure value on a player, driving a small amount of
/// distortion static via <c>RadiationDistortionSystem</c>. Server-only bookkeeping;
/// the actual visual effect is pushed onto the shared <c>DistortionVisionComponent</c>.
/// </summary>
[RegisterComponent]
public sealed partial class RadiationDistortionComponent : Component
{
    /// <summary>
    /// Current exposure, 0..1. Rises while taking radiation damage, decays back to 0
    /// once exposure stops.
    /// </summary>
    public float Exposure;
}
