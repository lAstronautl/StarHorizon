//-----------------------------------------------------------------------------
// Copyright 2024 Igor Spichkin
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//-----------------------------------------------------------------------------

using JetBrains.Annotations;

namespace Content.Server.ModularComputer;

public readonly record struct BinaryRw(byte[] Data)
{
    public readonly byte[] Data = Data;

    private void WriteInner(ReadOnlySpan<byte> value, int offset)
    {
        var length = Math.Min(Data.Length, value.Length);

        for (var i = 0; i < length; i++)
        {
            Data[offset + i] = value[i];
        }
    }

    private ReadOnlySpan<byte> ReadInner(int offset, int length)
    {
        var bytes = new byte[length];
        length = Math.Min(length, Data.Length);

        for (var i = 0; i < length; i++)
        {
            bytes[i] = Data[offset + i];
        }

        return bytes;
    }

    [PublicAPI]
    public void Write(byte value, int offset = 0)
    {
        Data[offset] = value;
    }

    [PublicAPI]
    public void Write(short value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(ushort value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(int value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(uint value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(long value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(ulong value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(double value, int offset = 0)
    {
        WriteInner(BitConverter.GetBytes(value), offset);
    }

    [PublicAPI]
    public void Write(bool value, int offset = 0)
    {
        Write(value ? (byte)1 : (byte)0, offset);
    }

    [PublicAPI]
    public void Write(ReadOnlySpan<byte> value, int offset = 0)
    {
        WriteInner(value, offset);
    }

    [PublicAPI]
    public byte ReadByte(int offset = 0)
    {
        return ReadInner(offset, 1)[0];
    }

    [PublicAPI]
    public short ReadShort(int offset = 0)
    {
        return BitConverter.ToInt16(ReadInner(offset, sizeof(short)));
    }

    [PublicAPI]
    public uint ReadUInt(int offset = 0)
    {
        return BitConverter.ToUInt32(ReadInner(offset, sizeof(uint)));
    }

    [PublicAPI]
    public int ReadInt(int offset = 0)
    {
        return BitConverter.ToInt32(ReadInner(offset, sizeof(int)));
    }

    [PublicAPI]
    public long ReadLong(int offset = 0)
    {
        return BitConverter.ToInt64(ReadInner(offset, sizeof(long)));
    }

    [PublicAPI]
    public ulong ReadULong(int offset = 0)
    {
        return BitConverter.ToUInt64(ReadInner(offset, sizeof(ulong)));
    }

    [PublicAPI]
    public double ReadDouble(int offset = 0)
    {
        return BitConverter.ToDouble(ReadInner(offset, sizeof(double)));
    }

    [PublicAPI]
    public bool ReadBool(int offset = 0)
    {
        return ReadByte(offset) != 0;
    }
}
