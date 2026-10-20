using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.GravityGun;

/// <summary>
/// A standalone shipbreaking gravity gun (independent from the tether and force guns).
/// Clicking a target applies the current <see cref="Mode"/>. Light, loose targets are moved;
/// heavy ones (grids, anchored structures) stay put and the user is moved instead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GravityGunSystem))]
public sealed partial class GravityGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public GravityGunMode Mode = GravityGunMode.Pull;

    /// <summary>
    /// How far the gun reaches.
    /// </summary>
    [DataField]
    public float MaxDistance = 15f;

    /// <summary>
    /// Targets with a mass above this are treated as heavy and are not moved by the gun.
    /// </summary>
    [DataField]
    public float MassLimit = 200f;

    /// <summary>
    /// Speed a light target gets in <see cref="GravityGunMode.Pull"/> and <see cref="GravityGunMode.Push"/>, in m/s.
    /// </summary>
    [DataField]
    public float MoveSpeed = 8f;

    /// <summary>
    /// Speed a light target gets in <see cref="GravityGunMode.Fire"/>, in m/s.
    /// </summary>
    [DataField]
    public float FireSpeed = 30f;

    /// <summary>
    /// The strength of the gun: the impulse applied to the user when the target is too heavy to move.
    /// </summary>
    [DataField]
    public float Power = 2500f;

    /// <summary>
    /// Multiplier of <see cref="Power"/> for the recoil of <see cref="GravityGunMode.Fire"/> at a heavy target.
    /// </summary>
    [DataField]
    public float FireRecoilMultiplier = 4f;

    /// <summary>
    /// Multiplier of <see cref="Power"/> for <see cref="GravityGunMode.Push"/> at a heavy target.
    /// </summary>
    [DataField]
    public float PushRecoilMultiplier = 0.5f;

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
}

public enum GravityGunMode : byte
{
    /// <summary>
    /// Drags the target towards the user. A heavy target drags the user towards itself.
    /// </summary>
    Pull,

    /// <summary>
    /// Pushes the target away from the user. Against a heavy target the user is pushed back a little.
    /// </summary>
    Push,

    /// <summary>
    /// Launches the target away at high speed. Against a heavy target the user is thrown back very hard.
    /// </summary>
    Fire,
}
