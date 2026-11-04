using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Виньетка (как у человека в крите) на экране пассажира дроп-пода. Уровень линейно меняется от <see cref="From"/> к <see cref="To"/>
/// между <see cref="Start"/> и <see cref="End"/> (по <see cref="Robust.Shared.Timing.IGameTiming.CurTime"/>),
/// поэтому по сети ничего не нужно слать каждый тик.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DropPodVignetteComponent : Component
{
    [DataField, AutoNetworkedField]
    public float From;

    [DataField, AutoNetworkedField]
    public float To;

    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    [DataField, AutoNetworkedField]
    public TimeSpan End;

    /// <summary>Капсула, в которой сидит пассажир. Только на сервере: по ней снимается виньетка.</summary>
    [ViewVariables]
    public EntityUid Pod;
}
