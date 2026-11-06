using Robust.Shared.GameStates;

namespace Content.Shared._StarHorizon.DropPod;

/// <summary>
/// Буфер привязки на мультитуле: запоминает первую выбранную сущность (капсулу или консоль),
/// чтобы вторым нажатием на парную сущность связать их.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DropPodLinkBufferComponent : Component
{
    [ViewVariables]
    public EntityUid? Pod;

    [ViewVariables]
    public EntityUid? Console;
}
