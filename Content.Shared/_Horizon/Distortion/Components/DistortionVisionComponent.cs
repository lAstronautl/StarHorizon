using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Distortion.Components;

/// <summary>
/// Applied to a mob standing on a shuttle affected by a distortion field whose
/// source has <c>AffectPlayers</c> set. Draws full-screen static on that player's client.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DistortionVisionComponent : Component
{
    /// <summary>
    /// Current noise intensity, from 0 to 1. Mirrors the affected shuttle's <see cref="DistortionAffectedComponent.Intensity"/>.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float Intensity;
}
