using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Дроп-под: закрытая капсула, в которую можно загрузить груз или залезть самому.
/// После запуска камера пассажиров плавно отдаляется, капсула уходит в блюспейс
/// и приземляется в точке, заданной на <see cref="DropPodConsoleComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodComponent : Component
{
    [ViewVariables]
    public DropPodState State = DropPodState.Idle;

    /// <summary>Когда текущая стадия началась (<see cref="Robust.Shared.Timing.IGameTiming.CurTime"/>).</summary>
    [ViewVariables]
    public TimeSpan StateStart;

    /// <summary>Когда текущая стадия закончится.</summary>
    [ViewVariables]
    public TimeSpan StateEnd;

    /// <summary>Запасная точка приземления, если метка пропала.</summary>
    [ViewVariables]
    public MapCoordinates Target;

    /// <summary>Если задан - капсула приземляется на этот грид (на случайный свободный тайл), где бы он ни находился.</summary>
    [ViewVariables]
    public EntityUid? TargetGrid;

    /// <summary>Метка приземления. Следует за гридом, над которым стоит.</summary>
    [ViewVariables]
    public EntityUid? Marker;

    /// <summary>Время обратного отсчёта перед стартом - успеть залезть и закрыть дверь.</summary>
    [DataField]
    public TimeSpan CountdownTime = TimeSpan.FromSeconds(8);

    /// <summary>Время, за которое камера отдаляется перед уходом в блюспейс.</summary>
    [DataField]
    public TimeSpan ZoomOutTime = TimeSpan.FromSeconds(3);

    /// <summary>Время в блюспейсе.</summary>
    [DataField]
    public TimeSpan FlightTime = TimeSpan.FromSeconds(4);

    /// <summary>Время, за которое экран светлеет на месте приземления (капсулы ещё нет).</summary>
    [DataField]
    public TimeSpan FadeInTime = TimeSpan.FromSeconds(1.5);

    /// <summary>Длительность падения капсулы и блокировки двери после неё.</summary>
    [DataField]
    public TimeSpan LandingTime = TimeSpan.FromSeconds(1);

    /// <summary>Максимальное отдаление камеры перед уходом в блюспейс.</summary>
    [DataField]
    public float PeakZoom = 3f;

    /// <summary>Радиус удара при приземлении.</summary>
    [DataField]
    public float LandingRadius = 1.2f;

    /// <summary>Урон всем, кто оказался под капсулой.</summary>
    [DataField]
    public DamageSpecifier LandingDamage = new();

    [DataField]
    public EntProtoId MarkerPrototype = "StarHorizonDropPodMarker";

    [DataField]
    public EntProtoId LaunchEffect = "EffectFlashBluespace";

    [DataField]
    public EntProtoId LandingEffect = "StarHorizonDropPodSmoke";

    [DataField]
    public SoundSpecifier LaunchSound = new SoundPathSpecifier("/Audio/_StarHorizon/Machines/droppod_woosh.ogg");

    [DataField]
    public SoundSpecifier LandingSound = new SoundPathSpecifier("/Audio/Effects/explosion1.ogg");
}

[Serializable, NetSerializable]
public enum DropPodState : byte
{
    /// <summary>Стоит и ждёт команды.</summary>
    Idle,
    /// <summary>Обратный отсчёт, дверь ещё можно открывать.</summary>
    Countdown,
    /// <summary>Камера отдаляется и затемняется, дверь заблокирована.</summary>
    ZoomingOut,
    /// <summary>Капсулы нет на карте, экран чёрный, эффекты сняты.</summary>
    InBluespace,
    /// <summary>Камера уже на месте посадки, экран светлеет. Капсулы и пассажиров ещё не видно.</summary>
    Arriving,
    /// <summary>Капсула падает и появляется вместе с пассажирами.</summary>
    Landing,
}

[Serializable, NetSerializable]
public enum DropPodVisuals : byte
{
    /// <summary>true - капсулы нет на карте (она в блюспейсе).</summary>
    InFlight,
    /// <summary>Текущая <see cref="DropPodState"/>, для подсветки и анимаций.</summary>
    State,
}
