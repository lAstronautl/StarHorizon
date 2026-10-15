using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Shuttles.Components;

/// <summary>
/// Shuttle grid that can fly over and land on a grid with <see cref="LandingPadComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LandableShuttleComponent : Component
{
    [DataField, AutoNetworkedField]
    public LandState State = LandState.Flying;

    /// <summary>
    /// Pad grid we are currently landed on.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Pad;

    /// <summary>
    /// Max linear speed (tiles/s) at which landing is allowed.
    /// </summary>
    [DataField]
    public float MaxLandingSpeed = 2f;
}

[Serializable, NetSerializable]
public enum LandState : byte
{
    Flying,
    Landed,
}
