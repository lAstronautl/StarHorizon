using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Затемнение экрана пассажира дроп-пода. Альфа линейно меняется от <see cref="From"/> к <see cref="To"/>
/// между <see cref="Start"/> и <see cref="End"/> (по <see cref="Robust.Shared.Timing.IGameTiming.CurTime"/>),
/// поэтому по сети ничего не нужно слать каждый тик.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DropPodFadeComponent : Component
{
    [DataField, AutoNetworkedField]
    public float From;

    [DataField, AutoNetworkedField]
    public float To;

    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    [DataField, AutoNetworkedField]
    public TimeSpan End;

    /// <summary>Капсула, в которой сидит пассажир. Только на сервере: по ней снимается затемнение.</summary>
    [ViewVariables]
    public EntityUid Pod;
}
