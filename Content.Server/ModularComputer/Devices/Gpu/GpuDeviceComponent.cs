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

using Content.Server.ModularComputer.Devices.Pci;
using Content.Shared.ModularComputer.Devices.Screen;
using Robust.Shared.Collections;
using Robust.Shared.Utility;
using SkiaSharp;

namespace Content.Server.ModularComputer.Devices.Gpu;

[RegisterComponent]
[Access(typeof(GpuDeviceSystem))]
public sealed partial class GpuDeviceComponent : PciDeviceComponent<GpuDeviceState>
{
    public const int MaxWidth = 720;
    public const int MaxHeight = 720;
    public const int Arguments = 10;
    public const int ArgumentsOffset = 0x100;

    /// <summary>
    ///     In bytes.
    /// </summary>
    public const int MaxVboMemory = 1_048_576;

    public override PciDevice Device { get; } = new("gpu", 0x1000, VendorId.AdvancedVideoDevices, DeviceId.Gpu);
}

[Access(typeof(GpuDeviceSystem))]
public sealed class GpuDeviceState : DeviceState, IDisposable
{
    [ViewVariables] public readonly double[] Arguments = new double[GpuDeviceComponent.Arguments];

    [ViewVariables] internal readonly Dictionary<uint, BaseObject> Objects = new();

    [ViewVariables] public CompressedBuffer CompressedBuffer = CompressedBuffer.Empty();

    [ViewVariables] public SKTypeface? DefaultTypeface;

    [ViewVariables] public SKSurface? Framebuffer;

    [ViewVariables] public uint FramebufferId;

    [ViewVariables] public int Height;

    [ViewVariables] public bool IsFramebufferDirty = false;

    [ViewVariables] public double OpResult = (int)GpuError.Ok;

    [ViewVariables] public SKPaint? Painter;

    [ViewVariables] public long UsedMemory;

    [ViewVariables] public int Width;

    public void Dispose()
    {
        var toDelete = new ValueList<uint>(Objects.Keys);

        foreach (var id in toDelete)
        {
            GpuDeviceSystem.TryDeleteObject(this, id);
        }

        Objects.Clear();
        Height = 0;
        Width = 0;
        Framebuffer?.Dispose();
        Framebuffer = null;
        Painter?.Dispose();
        Painter = null;
        DefaultTypeface?.Dispose();
        DefaultTypeface = null;
        UsedMemory = 0;
        OpResult = (int)GpuError.Ok;

        DebugTools.Assert(UsedMemory == 0);
    }
}

public enum GpuError
{
    Ok = 0,
    NotInitialized = -1,
    InvalidSize = -2,
    UnknownObject = -3,
    InvalidObject = -4,
    NotEnoughMemory = -5,
    Unknown = 0xFFFFFF
}

internal abstract class BaseObject : IDisposable
{
    [ViewVariables] public uint Id;

    [ViewVariables] public long Size;

    protected BaseObject(uint id, long size)
    {
        Id = id;
        Size = size;
    }

    public virtual void Dispose()
    {
    }
}

internal sealed class PointsObject : BaseObject
{
    [ViewVariables] public readonly SKPoint[] Points;

    public PointsObject(uint id, long size, SKPoint[] points) : base(id, size)
    {
        Points = points;
    }
}

internal sealed class TextObject : BaseObject
{
    [ViewVariables] public readonly string Text;

    public TextObject(uint id, long size, string text) : base(id, size)
    {
        Text = text;
    }
}

internal sealed class TypefaceObject : BaseObject
{
    [ViewVariables] public readonly SKTypeface Typeface;

    public TypefaceObject(uint id, long size, SKTypeface typeface) : base(id, size)
    {
        Typeface = typeface;
    }

    public override void Dispose()
    {
        Typeface.Dispose();
    }
}

internal sealed class ImageObject : BaseObject
{
    [ViewVariables] public readonly SKImage Image;

    public ImageObject(uint id, long size, SKImage image) : base(id, size)
    {
        Image = image;
    }

    public override void Dispose()
    {
        base.Dispose();

        Image.Dispose();
    }
}

internal sealed class SurfaceObject : BaseObject
{
    [ViewVariables] public readonly SKSurface Surface;

    public SurfaceObject(uint id, long size, SKSurface surface) : base(id, size)
    {
        Surface = surface;
    }

    public override void Dispose()
    {
        base.Dispose();

        Surface.Dispose();
    }
}
