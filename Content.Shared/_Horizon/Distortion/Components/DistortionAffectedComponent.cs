using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Distortion.Components;

/// <summary>
/// Applied to a shuttle grid by <c>DistortionFieldSystem</c> while it is within
/// range of one or more <see cref="DistortionFieldComponent"/> sources.
/// Consumed client-side to glitch out that shuttle's consoles.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DistortionAffectedComponent : Component
{
    /// <summary>
    /// Current noise intensity, from 0 (out of range) to 1 (shuttle fully inside the field).
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public float Intensity;

    /// <summary>
    /// Whether the source(s) affecting this shuttle also want the noise shown
    /// on the screens of players aboard it.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool AffectPlayers;
}
