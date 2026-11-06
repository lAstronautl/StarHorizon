using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Консоль управления дроп-подами. Капсулы привязываются мультитулом.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodConsoleComponent : Component
{
    /// <summary>
    /// Привязанные капсулы (мультитулом: нажать на капсулу и на консоль, повторная привязка отвязывает).
    /// Запускаются по очереди с интервалом <see cref="LaunchInterval"/>.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> LinkedPods = new();

    /// <summary>Интервал между стартами соседних капсул.</summary>
    [DataField]
    public TimeSpan LaunchInterval = TimeSpan.FromSeconds(1);

    /// <summary>Сколько нужно удерживать захват шаттла консолью.</summary>
    [DataField]
    public TimeSpan CaptureTime = TimeSpan.FromSeconds(15);

    /// <summary>Захват спадает, если шаттл дальше этого расстояния (в метрах) от консоли.</summary>
    [DataField]
    public float CaptureRange = 256f;

    /// <summary>Когда в открытое окно снова отправить позиции шаттлов.</summary>
    [ViewVariables]
    public TimeSpan NextUiUpdate;

    /// <summary>Грид, захват которого идёт сейчас.</summary>
    [ViewVariables]
    public EntityUid? CapturingGrid;

    [ViewVariables]
    public TimeSpan CaptureEnd;

    /// <summary>Захваченный грид: на него можно сбросить дроп-под, куда бы он ни улетел.</summary>
    [ViewVariables]
    public EntityUid? CapturedGrid;

    /// <summary>Максимальное расстояние до цели от начала координат карты.</summary>
    [DataField]
    public float MaxCoordinate = 20000f;
}
