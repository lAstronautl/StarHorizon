using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.Shuttles.Events;

/// <summary>
/// Sent from the shuttle console when the pilot presses the land / take off button.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleLandingRequest : BoundUserInterfaceMessage
{
    public bool TakeOff;
}
