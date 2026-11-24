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

    /// <summary>Радиус (в тайлах) вокруг приземлившейся капсулы, где расчищается порода.</summary>
    [DataField]
    public float ClearRadius = 3f;

    /// <summary>Через сколько после посадки дверца сама открывается.</summary>
    [DataField]
    public TimeSpan AutoOpenDelay = TimeSpan.FromSeconds(3);

    /// <summary>Когда дверца откроется сама (после посадки). Null: уже открыта или не садилась.</summary>
    [ViewVariables]
    public TimeSpan? OpenAt;

    /// <summary>Когда текущая стадия началась (<see cref="Robust.Shared.Timing.IGameTiming.CurTime"/>).</summary>
    [ViewVariables]
    public TimeSpan StateStart;

    /// <summary>Когда текущая стадия закончится.</summary>
    [ViewVariables]
    public TimeSpan StateEnd;

    /// <summary>
    /// План запуска, который консоль записывает в капсулу перед сигналом. Капсула стартует по
    /// <c>TriggerEvent</c> (сигнал DeviceLink или активация), если план есть. Только сервер.
    /// </summary>
    [ViewVariables]
    public DropPodLaunchPlan? Plan;

    /// <summary>Планета, на которую сбрасывается капсула (экспедиция планеты). Только сервер.</summary>
    [ViewVariables]
    public EntityUid? TargetPlanet;

    /// <summary>Капсула уже в пути, но экспедиция на планете ещё создаётся: ждём готовности карты.</summary>
    [ViewVariables]
    public bool WaitingForExpedition;

    [ViewVariables]
    public TimeSpan ExpeditionWaitEnd;

    /// <summary>Сколько максимум ждать, пока построится карта экспедиции, прежде чем сесть где придётся.</summary>
    [DataField]
    public TimeSpan ExpeditionWaitTime = TimeSpan.FromSeconds(90);

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

    /// <summary>
    /// Время в гиперпространстве (без фазы прибытия, её длину задаёт FTL). По умолчанию как у полного БСС
    /// (shuttle.travel_time 20 с = 15 с полёта + 5 с прибытия). Если карта экспедиции ещё строится, БСС удлиняется.
    /// </summary>
    [DataField]
    public TimeSpan FlightTime = TimeSpan.FromSeconds(15);

    /// <summary>Линейное демпфирование тела до полёта (в БСС оно снимается, чтобы капсула летела с постоянной скоростью).</summary>
    [ViewVariables]
    public float SavedLinearDamping = 0.2f;

    /// <summary>На сколько секунд за раз удлиняется БСС, пока строится карта экспедиции.</summary>
    [DataField]
    public TimeSpan FlightExtension = TimeSpan.FromSeconds(3);

    /// <summary>Когда БСС перестаёт удлиняться (карта так и не построилась): по <see cref="ExpeditionWaitTime"/> от начала удлинения.</summary>
    [ViewVariables]
    public TimeSpan? ExtendUntil;

    /// <summary>
    /// Камера уже на месте посадки (капсулы ещё нет): за это время виньетка снимается и зум возвращается,
    /// чтобы игрок увидел, куда падает. Когда виньетка снята, капсула физически приземляется.
    /// </summary>
    [DataField]
    public TimeSpan ArrivalViewTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Падение: анимация-сущность (диагональный спрайт Goobstation) и шлейф дыма, капсула в это время скрыта.
    /// Когда падение закончено, капсула появляется и приземляется. По умолчанию - длина спрайта.
    /// </summary>
    [DataField]
    public TimeSpan FallTime = TimeSpan.FromSeconds(DropPodLandingGeometry.TotalTime);

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

    /// <summary>Падающая капсула: анимированный диагональный спрайт, пока настоящая капсула скрыта.</summary>
    [DataField]
    public EntProtoId LandingAnimationEffect = "StarHorizonDropPodLandingAnimation";

    /// <summary>Клуб дыма: по траектории падения и при ударе.</summary>
    [DataField]
    public EntProtoId SmokePuffEffect = "StarHorizonDropPodSmokePuff";

    /// <summary>Сколько клубов дыма остаётся вдоль траектории, пока капсула падает.</summary>
    [DataField]
    public int TrailPuffCount = 9;

    /// <summary>Сколько клубов дыма вылетает при ударе.</summary>
    [DataField]
    public int ImpactPuffCount = 7;

    [DataField]
    public SoundSpecifier LandingAnimationSound = new SoundPathSpecifier("/Audio/_Goobstation/DropPod/supplypodlanding.ogg");

    /// <summary>Сколько клубов дыма вдоль траектории уже создано (только сервер).</summary>
    [ViewVariables]
    public int PuffsSpawned;

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
    /// <summary>Падение: капсулы ещё не видно, летит анимация и дым; потом капсула появляется и приземляется.</summary>
    Landing,
}

[Serializable, NetSerializable]
public enum DropPodVisuals : byte
{
    /// <summary>true - капсулы нет на карте (она в блюспейсе).</summary>
    InFlight,
    /// <summary>Текущая <see cref="DropPodState"/>, для подсветки и анимаций.</summary>
    State,
    /// <summary>true - капсула приземлилась: вместо обычного спрайта рисуется спрайт упавшей капсулы (как у Goobstation).</summary>
    Landed,
}

/// <summary>Куда и когда должна лететь капсула (задаёт консоль).</summary>
public sealed class DropPodLaunchPlan
{
    /// <summary>Мировая точка посадки.</summary>
    public MapCoordinates Target;

    /// <summary>Точка метки в координатах грида, если летим на захваченный шаттл.</summary>
    public EntityCoordinates? Coords;

    /// <summary>Захваченный шаттл, если летим на него.</summary>
    public EntityUid? Grid;

    /// <summary>Планета с экспедицией, если сбрасываемся на неё (карта экспедиции создаётся при взлёте).</summary>
    public EntityUid? Planet;

    /// <summary>Очередь запуска: на сколько позже обычного отсчёта стартует эта капсула.</summary>
    public TimeSpan Delay;
}
