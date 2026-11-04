using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._StarHorizon.DropPod;

[Serializable, NetSerializable]
public enum DropPodUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class DropPodConsoleState(
    bool linked,
    DropPodState podState,
    TimeSpan stateEnd,
    int occupants,
    Vector2 consolePosition,
    Vector2? target,
    string? error) : BoundUserInterfaceState
{
    public bool Linked = linked;
    public DropPodState PodState = podState;
    public TimeSpan StateEnd = stateEnd;
    public int Occupants = occupants;
    /// <summary>Мировые координаты консоли - удобная отправная точка.</summary>
    public Vector2 ConsolePosition = consolePosition;
    public Vector2? Target = target;
    public string? Error = error;
}

/// <summary>Запустить капсулу в указанные мировые координаты.</summary>
[Serializable, NetSerializable]
public sealed class DropPodLaunchMessage(Vector2 target) : BoundUserInterfaceMessage
{
    public Vector2 Target = target;
}

[Serializable, NetSerializable]
public sealed class DropPodAbortMessage : BoundUserInterfaceMessage;
