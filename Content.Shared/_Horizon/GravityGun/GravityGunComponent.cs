using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.GravityGun;

/// <summary>
/// A standalone shipbreaking gravity gun (independent from the tether and force guns).
/// Click a target to grab it: the target follows the cursor and the mouse wheel moves it away from or towards you.
/// A target that is too heavy to move (a big grid) stays where it is and the user is pulled instead.
/// Click again to launch what is held, or use the gun in hand to let go.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GravityGunComponent : Component
{
    /// <summary>
    /// How far the gun reaches, both for grabbing and for holding.
    /// </summary>
    [DataField]
    public float MaxDistance = 15f;

    /// <summary>
    /// How close to the user a target can be held.
    /// </summary>
    [DataField]
    public float MinHoldDistance = 1.5f;

    /// <summary>
    /// How far the wheel moves the hold distance per notch, in tiles.
    /// </summary>
    [DataField]
    public float WheelStep = 1f;

    /// <summary>
    /// Loose entities heavier than this are not moved, the user is pulled to them instead.
    /// </summary>
    [DataField]
    public float MassLimit = 200f;

    /// <summary>
    /// Grids heavier than this are not moved, the user is pulled to them instead.
    /// </summary>
    [DataField]
    public float GridMassLimit = 5000f;

    /// <summary>
    /// The fastest a held target is brought to where the gun wants it, in m/s.
    /// </summary>
    [DataField]
    public float HoldSpeed = 10f;

    /// <summary>
    /// How hard the gun pulls a held target, in newtons. Heavier targets are slower to bring around.
    /// </summary>
    [DataField]
    public float MaxForce = 6000f;

    /// <summary>
    /// How hard the gun pulls the user towards a target that is too heavy to move, in newtons.
    /// </summary>
    [DataField]
    public float UserForce = 1500f;

    /// <summary>
    /// How quickly the held distance is closed or opened, in 1/s.
    /// </summary>
    [DataField]
    public float Gain = 4f;

    /// <summary>
    /// Speed a light target gets when launched, in m/s.
    /// </summary>
    [DataField]
    public float FireSpeed = 30f;

    /// <summary>
    /// The strength of the gun: the impulse that throws the user back when launching at something too heavy to move.
    /// </summary>
    [DataField]
    public float Power = 2500f;

    /// <summary>
    /// Multiplier of <see cref="Power"/> for the recoil of launching at a heavy target.
    /// </summary>
    [DataField]
    public float FireRecoilMultiplier = 4f;

    /// <summary>
    /// The user never gets faster than this from the gun, in m/s.
    /// </summary>
    [DataField]
    public float MaxUserSpeed = 50f;

    /// <summary>
    /// Whether living creatures can be moved by the gun.
    /// </summary>
    [DataField]
    public bool CanAffectMobs;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Weapons/soup.ogg");

    /// <summary>
    /// What is currently held: a loose entity or a grid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Held;

    /// <summary>
    /// The grabbed point, in the local space of <see cref="Held"/>, so it follows the target as it moves and turns.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 HeldPoint;

    /// <summary>
    /// How far from the user the target is held.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HoldDistance;

    /// <summary>
    /// Whether the target is too heavy to move, so the user is moved instead.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool UserIsMoved;

    /// <summary>
    /// The direction the user is aiming in, sent by the client.
    /// </summary>
    [ViewVariables]
    public Vector2 Aim;
}

/// <summary>
/// Sent by the client while holding something: where the cursor is.
/// </summary>
[Serializable, NetSerializable]
public sealed class GravityGunAimEvent : EntityEventArgs
{
    public NetCoordinates Coordinates;
}

/// <summary>
/// Sent by the client when the mouse wheel moves the held target. Positive moves it away, negative towards the user.
/// </summary>
[Serializable, NetSerializable]
public sealed class GravityGunDistanceEvent : EntityEventArgs
{
    public float Delta;
}
