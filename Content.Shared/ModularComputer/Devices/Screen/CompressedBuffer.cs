using Robust.Shared.Serialization;

namespace Content.Shared.ModularComputer.Devices.Screen;

[Serializable, NetSerializable]
public sealed class CompressedBuffer
{
    public byte[] Data;
    public int UncompressedSize;

    public CompressedBuffer(byte[] data, int uncompressedSize)
    {
        Data = data;
        UncompressedSize = uncompressedSize;
    }

    public static CompressedBuffer Empty()
    {
        return new CompressedBuffer(Array.Empty<byte>(), 0);
    }

    public bool IsEmpty()
    {
        return Data.Length == 0;
    }
}
