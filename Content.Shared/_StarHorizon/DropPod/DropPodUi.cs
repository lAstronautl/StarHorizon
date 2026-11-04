using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.DropPod;

[Serializable, NetSerializable]
public enum DropPodUiKey : byte
{
    Key,
}

/// <summary>Шаттл, доступный для захвата.</summary>
[Serializable, NetSerializable]
public readonly record struct DropPodGridInfo(NetEntity Grid, string Name, Vector2 Position);

[Serializable, NetSerializable]
public sealed class DropPodConsoleState(
    bool linked,
    DropPodState podState,
    TimeSpan stateEnd,
    TimeSpan stateStart,
    int occupants,
    Vector2 consolePosition,
    Vector2? target,
    string? error,
    List<DropPodGridInfo> grids,
    NetEntity? capturingGrid,
    NetEntity? capturedGrid,
    TimeSpan captureEnd,
    TimeSpan captureTime) : BoundUserInterfaceState
{
    public bool Linked = linked;
    public DropPodState PodState = podState;
    public TimeSpan StateEnd = stateEnd;
    public TimeSpan StateStart = stateStart;
    public int Occupants = occupants;
    /// <summary>Мировые координаты консоли - удобная отправная точка.</summary>
    public Vector2 ConsolePosition = consolePosition;
    public Vector2? Target = target;
    public string? Error = error;
    public List<DropPodGridInfo> Grids = grids;
    public NetEntity? CapturingGrid = capturingGrid;
    public NetEntity? CapturedGrid = capturedGrid;
    public TimeSpan CaptureEnd = captureEnd;
    public TimeSpan CaptureTime = captureTime;
}

/// <summary>Запустить капсулу в указанные мировые координаты.</summary>
[Serializable, NetSerializable]
public sealed class DropPodLaunchMessage(Vector2 target) : BoundUserInterfaceMessage
{
    public Vector2 Target = target;
}

/// <summary>Запустить капсулу на захваченный шаттл (на случайный свободный тайл).</summary>
[Serializable, NetSerializable]
public sealed class DropPodLaunchToCapturedMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class DropPodAbortMessage : BoundUserInterfaceMessage;

/// <summary>Начать захват шаттла (занимает <see cref="DropPodConsoleComponent.CaptureTime"/>).</summary>
[Serializable, NetSerializable]
public sealed class DropPodCaptureMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}

/// <summary>Сбросить захват или его прогресс.</summary>
[Serializable, NetSerializable]
public sealed class DropPodReleaseMessage : BoundUserInterfaceMessage;
