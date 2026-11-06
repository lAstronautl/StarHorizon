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

    /// <summary>Капсула одноразовая: после посадки она прикручивается к полу и больше не запускается.</summary>
    [ViewVariables]
    public bool Used;

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

    /// <summary>Стартовая фаза FTL: за это время камера отдаляется и нарастает виньетка.</summary>
    [DataField]
    public TimeSpan ZoomOutTime = TimeSpan.FromSeconds(5);

    /// <summary>Через сколько после начала взлёта начинает расти виньетка (камера отдаляется сразу).</summary>
    [DataField]
    public TimeSpan VignetteDelay = TimeSpan.FromSeconds(2);

    /// <summary>Время в гиперпространстве (без фазы прибытия, её длину задаёт FTL).</summary>
    [DataField]
    public TimeSpan FlightTime = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Камера уже на месте посадки (капсулы ещё нет): за это время виньетка снимается и зум возвращается,
    /// чтобы игрок увидел, куда падает. Когда виньетка снята, капсула физически приземляется.
    /// </summary>
    [DataField]
    public TimeSpan ArrivalViewTime = TimeSpan.FromSeconds(3);

    /// <summary>Падение капсулы и блокировка двери после касания.</summary>
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

    /// <summary>Длинный шлейф позади падающей капсулы.</summary>
    [DataField]
    public EntProtoId TrailEffect = "StarHorizonDropPodTrail";

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
    /// <summary>Камера отдаляется, виньетка нарастает, дверь заблокирована.</summary>
    ZoomingOut,
    /// <summary>Капсула летит по блюспейсу (FTL-карта), у пассажиров виньетка как в крите.</summary>
    InBluespace,
    /// <summary>Камера переброшена на место посадки, виньетка снимается, капсулы ещё не видно.</summary>
    Arriving,
    /// <summary>Виньетка снята: капсула физически приземляется с анимацией падения.</summary>
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
