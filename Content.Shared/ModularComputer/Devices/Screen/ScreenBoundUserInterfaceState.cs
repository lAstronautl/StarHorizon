using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public sealed class ScreenBoundUserInterfaceState : BoundUserInterfaceState
{
    public int Width;
    public int Height;
    public CompressedBuffer? Framebuffer;
    public bool SendMouseEvents;
    public bool SendKeyboardEvents;
    public Color BorderColor;
    public Color LabelColor;
    public string Label;

    public ScreenBoundUserInterfaceState(int width, int height, CompressedBuffer? framebuffer, bool sendMouseEvents,
        bool sendKeyboardEvents, Color borderColor, Color labelColor, string label)
    {
        Width = width;
        Height = height;
        Framebuffer = framebuffer;
        SendMouseEvents = sendMouseEvents;
        SendKeyboardEvents = sendKeyboardEvents;
        BorderColor = borderColor;
        LabelColor = labelColor;
        Label = label;
    }
}
