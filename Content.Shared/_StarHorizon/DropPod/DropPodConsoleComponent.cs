using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Консоль управления дроп-подами. Капсула привязывается мультитулом.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodConsoleComponent : Component
{
    /// <summary>Привязанная капсула. Привязывается мультитулом (по очереди нажать на капсулу и консоль).</summary>
    [ViewVariables]
    public EntityUid? LinkedPod;

    /// <summary>Сколько нужно удерживать захват шаттла консолью.</summary>
    [DataField]
    public TimeSpan CaptureTime = TimeSpan.FromSeconds(15);

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
