using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public sealed class MouseMoveMessage : BoundUserInterfaceMessage
{
    public readonly Vector2 Position;

    public MouseMoveMessage(Vector2 position)
    {
        Position = position;
    }
}
