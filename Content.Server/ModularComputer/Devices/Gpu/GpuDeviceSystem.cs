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

using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using Content.Server.ModularComputer.Cpu;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.NTVM;
using Content.Shared.ModularComputer.Devices.Screen;
using JetBrains.Annotations;
using Prometheus;
using Robust.Shared.Collections;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SkiaSharp;

namespace Content.Server.ModularComputer.Devices.Gpu;

public sealed class GpuDeviceSystem : PciDeviceSystem<GpuDeviceComponent, GpuDeviceState>
{
    private static readonly Gauge GpuMemoryUsage =
        Metrics.CreateGauge("machines_gpu_memory_usage", "Memory used by machines GPU (in bytes)");

    private static string _fontPath = string.Empty;

    [Dependency] private readonly IResourceManager _resource = default!;

    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextMetricsUpdate = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GpuDeviceComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<GpuDeviceComponent, MachineTurnedOffEvent>(OnMachineTurnedOff);

        foreach (var root in _resource.GetContentRoots())
        {
            var fontPath = Path.GetFullPath("Fonts/Greybeard/Greybeard-14px.ttf", root.CanonPath);

            if (Path.Exists(fontPath))
            {
                _fontPath = fontPath;
                break;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextMetricsUpdate)
            return;

        _nextMetricsUpdate = _timing.CurTime + TimeSpan.FromSeconds(10);
        var memoryUsed = 0L;

        var query = EntityQueryEnumerator<GpuDeviceComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            memoryUsed += UpdateState(uid, component, state => state.UsedMemory);
        }

        GpuMemoryUsage.Set(memoryUsed);
    }

    private void OnMachineTurnedOff(EntityUid uid, GpuDeviceComponent component, ref MachineTurnedOffEvent args)
    {
        UpdateState(uid, component, state => { state.Dispose(); });
    }

    private void OnComponentShutdown(EntityUid uid, GpuDeviceComponent component, ComponentShutdown args)
    {
        UpdateState(uid, component, state => { state.Dispose(); });
    }

    [PublicAPI]
    public bool IsFramebufferDirty(EntityUid uid, GpuDeviceComponent component)
    {
        return UpdateState(uid, component, state => state.IsFramebufferDirty);
    }

    [PublicAPI]
    public CompressedBuffer GetFramebufferAndMarkClean(EntityUid uid, GpuDeviceComponent? component)
    {
        if (!Resolve(uid, ref component))
            return CompressedBuffer.Empty();

        return UpdateState(uid, component, state =>
        {
            state.IsFramebufferDirty = false;
            return state.CompressedBuffer;
        });
    }

    private static SKSurface? GetDrawingSurface(GpuDeviceState state)
    {
        var surfaceId = state.FramebufferId;
        SKSurface? surface = null;

        if (surfaceId == 0)
            surface = state.Framebuffer;
        else if (state.Objects.TryGetValue(surfaceId, out var obj) && obj is SurfaceObject surfaceObject)
            surface = surfaceObject.Surface;

        return surface;
    }

    private static void OpDrawPixel(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x = (float)args[0];
        var y = (float)args[1];

        surface.Canvas.DrawPoint(x, y, state.Painter.Color);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawLine(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x0 = (float)args[0];
        var y0 = (float)args[1];
        var x1 = (float)args[2];
        var y1 = (float)args[3];

        surface.Canvas.DrawLine(x0, y0, x1, y1, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPixel(GpuDeviceState state)
    {
        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x = (int)args[0];
        var y = (int)args[1];

        var pixels = surface.PeekPixels();
        var color = pixels.GetPixelColor(x, y);

        state.OpResult = color.Red | (color.Green << 4) | (color.Blue << 8) | (color.Alpha << 12);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpInit(GpuDeviceState state)
    {
        var args = state.Arguments;
        var width = (int)Math.Clamp(args[0], 0, GpuDeviceComponent.MaxWidth);
        var height = (int)Math.Clamp(args[1], 0, GpuDeviceComponent.MaxHeight);

        if (state.Width == width && state.Height == height)
            return;

        if (width <= 0 || height <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        state.Dispose();

        state.Width = width;
        state.Height = height;
        state.Framebuffer = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        state.DefaultTypeface = SKTypeface.FromFile(_fontPath);
        state.Painter = new SKPaint();
        state.Painter.Typeface = state.DefaultTypeface;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpSetPainterColor(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var r = (byte)args[0];
        var g = (byte)args[1];
        var b = (byte)args[2];
        var a = (byte)args[3];

        state.Painter.Color = new SKColor(r, g, b, a);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterColor(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var color = state.Painter.Color;

        state.OpResult = (ulong)((color.Alpha << 24) | (color.Blue << 16) | (color.Green << 8) | color.Red);
    }

    private static void OpSetPainterStyle(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var style = (int)args[0] switch
        {
            1 => SKPaintStyle.Stroke,
            2 => SKPaintStyle.StrokeAndFill,
            _ => SKPaintStyle.Fill
        };

        state.Painter.Style = style;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterStyle(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var style = state.Painter.Style switch
        {
            SKPaintStyle.Stroke => 1,
            SKPaintStyle.StrokeAndFill => 2,
            _ => 0
        };

        state.OpResult = style;
    }

    private static void OpSetPainterBlendMode(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var blendMode = (int)args[0] switch
        {
            1 => SKBlendMode.Src,
            2 => SKBlendMode.Dst,
            3 => SKBlendMode.SrcOver,
            4 => SKBlendMode.DstOver,
            5 => SKBlendMode.SrcIn,
            6 => SKBlendMode.DstIn,
            7 => SKBlendMode.SrcOut,
            8 => SKBlendMode.DstOut,
            9 => SKBlendMode.SrcATop,
            10 => SKBlendMode.DstATop,
            11 => SKBlendMode.Xor,
            12 => SKBlendMode.Plus,
            13 => SKBlendMode.Modulate,
            14 => SKBlendMode.Screen,
            15 => SKBlendMode.Overlay,
            16 => SKBlendMode.Darken,
            17 => SKBlendMode.Lighten,
            18 => SKBlendMode.ColorDodge,
            19 => SKBlendMode.ColorBurn,
            20 => SKBlendMode.HardLight,
            21 => SKBlendMode.SoftLight,
            22 => SKBlendMode.Difference,
            23 => SKBlendMode.Exclusion,
            24 => SKBlendMode.Multiply,
            25 => SKBlendMode.Hue,
            26 => SKBlendMode.Saturation,
            27 => SKBlendMode.Color,
            28 => SKBlendMode.Luminosity,
            _ => SKBlendMode.Clear
        };

        state.Painter.BlendMode = blendMode;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterBlendMode(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.BlendMode switch
        {
            SKBlendMode.Src => 1,
            SKBlendMode.Dst => 2,
            SKBlendMode.SrcOver => 3,
            SKBlendMode.DstOver => 4,
            SKBlendMode.SrcIn => 5,
            SKBlendMode.DstIn => 6,
            SKBlendMode.SrcOut => 7,
            SKBlendMode.DstOut => 8,
            SKBlendMode.SrcATop => 9,
            SKBlendMode.DstATop => 10,
            SKBlendMode.Xor => 11,
            SKBlendMode.Plus => 12,
            SKBlendMode.Modulate => 13,
            SKBlendMode.Screen => 14,
            SKBlendMode.Overlay => 15,
            SKBlendMode.Darken => 16,
            SKBlendMode.Lighten => 17,
            SKBlendMode.ColorDodge => 18,
            SKBlendMode.ColorBurn => 19,
            SKBlendMode.HardLight => 20,
            SKBlendMode.SoftLight => 21,
            SKBlendMode.Difference => 22,
            SKBlendMode.Exclusion => 23,
            SKBlendMode.Multiply => 24,
            SKBlendMode.Hue => 25,
            SKBlendMode.Saturation => 26,
            SKBlendMode.Color => 27,
            SKBlendMode.Luminosity => 28,
            _ => 0
        };
    }

    private static void OpSetPainterFilterQuality(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var quality = (int)args[0] switch
        {
            1 => SKFilterQuality.Low,
            2 => SKFilterQuality.Medium,
            3 => SKFilterQuality.High,
            _ => SKFilterQuality.None
        };

        state.Painter.FilterQuality = quality;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterFilterQuality(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var quality = state.Painter.FilterQuality switch
        {
            SKFilterQuality.Low => 1,
            SKFilterQuality.Medium => 2,
            SKFilterQuality.High => 3,
            _ => 0
        };

        state.OpResult = quality;
    }

    private static void OpSetPainterHintingLevel(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var level = (int)args[0] switch
        {
            1 => SKPaintHinting.Slight,
            2 => SKPaintHinting.Normal,
            3 => SKPaintHinting.Full,
            _ => SKPaintHinting.NoHinting
        };

        state.Painter.HintingLevel = level;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterHintingLevel(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var level = state.Painter.HintingLevel switch
        {
            SKPaintHinting.Slight => 1,
            SKPaintHinting.Normal => 2,
            SKPaintHinting.Full => 3,
            _ => 0
        };

        state.OpResult = level;
    }

    private static void OpSetPainterAutohinting(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var newState = (int)args[0] != 0;

        state.Painter.IsAutohinted = newState;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterAutohinting(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.IsAutohinted ? 1 : 0;
    }

    private static void OpSetPainterAntialiasing(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var newState = (int)args[0] != 0;

        state.Painter.IsAntialias = newState;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterAntialiasing(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.IsAntialias ? 1 : 0;
    }

    private static void OpSetPainterDithering(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var newState = (int)args[0] != 0;

        state.Painter.IsDither = newState;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterDithering(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.IsDither ? 1 : 0;
    }

    private static void OpMeasureTextHeight(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bufferId = (uint)args[0];

        if (!state.Objects.TryGetValue(bufferId, out var buffer) || buffer is not TextObject textObject)
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        var bounds = new SKRect();
        state.Painter.MeasureText(textObject.Text, ref bounds);
        state.OpResult = bounds.Height;
    }

    private static void OpMeasureTextWidth(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bufferId = (uint)args[0];

        if (!state.Objects.TryGetValue(bufferId, out var buffer) || buffer is not TextObject textObject)
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        var bounds = new SKRect();
        state.Painter.MeasureText(textObject.Text, ref bounds);
        state.OpResult = bounds.Width;
    }

    private static void OpSetPainterTypeface(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bufferId = (uint)args[0];

        if (bufferId == 0)
        {
            state.Painter.Typeface = state.DefaultTypeface;
            state.OpResult = (int)GpuError.Ok;
            return;
        }

        if (!state.Objects.TryGetValue(bufferId, out var buffer) || buffer is not TypefaceObject fontObject)
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        state.Painter.Typeface = fontObject.Typeface;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterTypeface(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (state.Painter.Typeface is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (state.Painter.Typeface == state.DefaultTypeface)
        {
            state.OpResult = 0;
            return;
        }

        var typeface = state.Objects.Values.First(obj =>
            obj is TypefaceObject typefaceObject && typefaceObject.Typeface == state.Painter.Typeface);

        state.OpResult = typeface.Id;
    }

    private static void OpSetPainterTextSize(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var size = (float)args[0];

        state.Painter.TextSize = size;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterTextSize(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.TextSize;
    }

    private static void OpSetPainterTextScaleX(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var scale = (float)args[0];

        state.Painter.TextScaleX = scale;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterTextScaleX(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.TextScaleX;
    }

    private static void OpSetPainterTextSkewX(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var skew = (float)args[0];

        state.Painter.TextSkewX = skew;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterTextSkewX(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.TextSkewX;
    }

    private static void OpSetPainterTextAlign(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var align = (SKTextAlign)args[0];

        state.Painter.TextAlign = align;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterTextAlign(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = (double)state.Painter.TextAlign;
    }

    private static void OpSetPainterSubpixelText(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var newState = (int)args[0] == 1;

        state.Painter.SubpixelText = newState;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpGetPainterSubpixelText(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        state.OpResult = state.Painter.SubpixelText ? 1.0 : 0.0;
    }

    private static void OpMeasureStringHeight(Machine machine, GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var address = (ulong)args[0];
        var length = (int)args[1];

        var data = machine.ReadRam(address, length);
        var str = Encoding.UTF8.GetString(data);

        var bounds = new SKRect();
        state.Painter.MeasureText(str, ref bounds);
        state.OpResult = bounds.Height;
    }

    private static void OpMeasureStringWidth(Machine machine, GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var address = (ulong)args[0];
        var length = (int)args[1];

        var data = machine.ReadRam(address, length);
        var str = Encoding.UTF8.GetString(data);

        var bounds = new SKRect();
        state.Painter.MeasureText(str, ref bounds);
        state.OpResult = bounds.Width;
    }

    private static void OpDrawRect(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x = (float)args[0];
        var y = (float)args[1];
        var w = (float)args[2];
        var h = (float)args[3];

        surface.Canvas.DrawRect(x, y, w, h, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawRoundRect(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x = (float)args[0];
        var y = (float)args[1];
        var w = (float)args[2];
        var h = (float)args[3];
        var rx = (float)args[4];
        var ry = (float)args[5];

        surface.Canvas.DrawRoundRect(x, y, w, h, rx, ry, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawCircle(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var cx = (float)args[0];
        var cy = (float)args[1];
        var r = (float)args[2];

        surface.Canvas.DrawCircle(cx, cy, r, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawOval(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var cx = (float)args[0];
        var cy = (float)args[1];
        var rx = (float)args[2];
        var ry = (float)args[3];

        surface.Canvas.DrawOval(cx, cy, rx, ry, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpCreatePointsObject(Machine machine, GpuDeviceState state)
    {
        if (GetNextId(state) is not { } nextId)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var args = state.Arguments;
        var address = (ulong)args[0];
        var size = (int)args[1];
        var length = (int)args[2];
        var objectSize = (long)(size * length);

        if (objectSize <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        if (state.UsedMemory + objectSize > GpuDeviceComponent.MaxVboMemory)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var data = new BinaryRw(machine.ReadRam(address, size * length));

        if (size != sizeof(double) * 2)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        var idx = 0;
        var points = new SKPoint[length];

        for (var i = 0; i < length; i++)
        {
            var x = (float)data.ReadDouble(idx * sizeof(double));
            var y = (float)data.ReadDouble((idx + 1) * sizeof(double));

            points[i] = new SKPoint(x, y);
            idx += 2;
        }

        state.Objects.Add(nextId, new PointsObject(nextId, objectSize, points));
        state.UsedMemory += size * length;
        state.OpResult = nextId;
    }

    private static uint? GetNextId(GpuDeviceState state)
    {
        for (var id = 1u; id < uint.MaxValue; id++)
        {
            if (!state.Objects.ContainsKey(id))
                return id;
        }

        return null;
    }

    internal static bool TryDeleteObject(GpuDeviceState state, uint id)
    {
        if (!state.Objects.Remove(id, out var obj))
            return false;

        state.UsedMemory -= obj.Size;

        switch (obj)
        {
            case TypefaceObject typefaceObject:
            {
                if (state.Painter?.Typeface == typefaceObject.Typeface)
                    state.Painter.Typeface = state.DefaultTypeface;
                break;
            }
            case SurfaceObject surfaceObject:
            {
                if (state.FramebufferId == surfaceObject.Id) state.FramebufferId = 0;

                break;
            }
        }

        obj.Dispose();

        return true;
    }

    private static void OpDeleteObject(GpuDeviceState state)
    {
        var args = state.Arguments;
        var id = (uint)args[0];

        if (!TryDeleteObject(state, id))
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawPoints(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bufferId = (uint)args[0];
        var mode = (int)args[1] switch
        {
            1 => SKPointMode.Lines,
            2 => SKPointMode.Polygon,
            _ => SKPointMode.Points
        };

        if (!state.Objects.TryGetValue(bufferId, out var buffer) || buffer is not PointsObject pointsObject)
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        surface.Canvas.DrawPoints(mode, pointsObject.Points, state.Painter);
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawText(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bufferId = (uint)args[0];
        var x = (float)args[1];
        var y = (float)args[2];

        if (!state.Objects.TryGetValue(bufferId, out var buffer) || buffer is not TextObject textObject)
        {
            state.OpResult = (int)GpuError.UnknownObject;
            return;
        }

        surface.Canvas.DrawText(textObject.Text, x, y, state.Painter);
    }

    private static void OpDrawImage(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bitmapId = (uint)args[0];
        var x = (float)args[1];
        var y = (float)args[2];

        if (!state.Objects.TryGetValue(bitmapId, out var obj) || obj is not ImageObject imageObject)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        surface.Canvas.DrawImage(imageObject.Image, x, y, state.Painter);

        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawImageRect(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bitmapId = (uint)args[0];
        var left = (float)args[1];
        var top = (float)args[2];
        var right = (float)args[3];
        var bottom = (float)args[4];

        if (!state.Objects.TryGetValue(bitmapId, out var obj) || obj is not ImageObject imageObject)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        surface.Canvas.DrawImage(imageObject.Image, new SKRect(left, top, right, bottom), state.Painter);

        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawImageRectSrc(GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var bitmapId = (uint)args[0];
        var srcLeft = (float)args[1];
        var srcTop = (float)args[2];
        var srcRight = (float)args[3];
        var srcBottom = (float)args[4];
        var left = (float)args[5];
        var top = (float)args[6];
        var right = (float)args[7];
        var bottom = (float)args[8];

        if (!state.Objects.TryGetValue(bitmapId, out var obj) || obj is not ImageObject imageObject)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        surface.Canvas.DrawImage(imageObject.Image, new SKRect(srcLeft, srcTop, srcRight, srcBottom),
            new SKRect(left, top, right, bottom), state.Painter);

        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDrawString(Machine machine, GpuDeviceState state)
    {
        if (state.Painter is null)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        if (GetDrawingSurface(state) is not { } surface)
        {
            state.OpResult = (int)GpuError.NotInitialized;
            return;
        }

        var args = state.Arguments;
        var x = (float)args[0];
        var y = (float)args[1];
        var address = (ulong)args[2];
        var length = (int)args[3];

        var data = new BinaryRw(machine.ReadRam(address, length));
        var str = Encoding.UTF8.GetString(data.Data);

        surface.Canvas.DrawText(str, x, y, state.Painter);

        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpDeleteAllObjects(GpuDeviceState state)
    {
        var toDelete = new ValueList<uint>(state.Objects.Keys);

        foreach (var id in toDelete)
        {
            TryDeleteObject(state, id);
        }

        DebugTools.Assert(state.UsedMemory == 0);
    }

    private static void OpCreateImageObject(Machine machine, GpuDeviceState state)
    {
        if (GetNextId(state) is not { } nextId)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var args = state.Arguments;
        var width = (int)args[0];
        var height = (int)args[1];
        var address = (ulong)args[2];
        var objectSize = width * height * 4;

        if (objectSize <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        if (state.UsedMemory + objectSize > GpuDeviceComponent.MaxVboMemory)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var data = new BinaryRw(machine.ReadRam(address, objectSize));

        var image = SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            data.Data);

        if (image is null)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        state.UsedMemory += objectSize;
        state.Objects.Add(nextId, new ImageObject(nextId, objectSize, image));
        state.OpResult = nextId;
    }

    private static void OpCreateSurfaceObject(GpuDeviceState state)
    {
        if (GetNextId(state) is not { } nextId)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var args = state.Arguments;
        var width = (int)args[0];
        var height = (int)args[1];
        var objectSize = width * height * 4;

        if (objectSize <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        if (state.UsedMemory + objectSize > GpuDeviceComponent.MaxVboMemory)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        if (surface is null)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        state.Objects.Add(nextId, new SurfaceObject(nextId, objectSize, surface));
        state.UsedMemory += objectSize;
        state.OpResult = nextId;
    }

    private static void OpSwitchSurface(GpuDeviceState state)
    {
        var args = state.Arguments;
        var objectId = (uint)args[0];

        if (objectId != 0)
        {
            if (!state.Objects.TryGetValue(objectId, out var obj) || obj is not SurfaceObject)
            {
                state.OpResult = (int)GpuError.UnknownObject;
                return;
            }
        }

        state.FramebufferId = objectId;
        state.OpResult = (int)GpuError.Ok;
    }

    private static void OpCreateTypefaceObject(Machine machine, GpuDeviceState state)
    {
        if (GetNextId(state) is not { } nextId)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var args = state.Arguments;
        var address = (ulong)args[0];
        var size = (int)args[1];

        if (size <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        if (state.UsedMemory + size > GpuDeviceComponent.MaxVboMemory)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var data = new BinaryRw(machine.ReadRam(address, size));
        var typeface = SKTypeface.FromStream(new MemoryStream(data.Data));

        if (typeface is null)
        {
            state.OpResult = (int)GpuError.InvalidObject;
            return;
        }

        state.Objects.Add(nextId, new TypefaceObject(nextId, size, typeface));
        state.UsedMemory += size;
        state.OpResult = nextId;
    }

    private static void OpCreateTextObject(Machine machine, GpuDeviceState state)
    {
        if (GetNextId(state) is not { } nextId)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var args = state.Arguments;
        var address = (ulong)args[0];
        var size = (int)args[1];

        if (size <= 0)
        {
            state.OpResult = (int)GpuError.InvalidSize;
            return;
        }

        if (state.UsedMemory + size > GpuDeviceComponent.MaxVboMemory)
        {
            state.OpResult = (int)GpuError.NotEnoughMemory;
            return;
        }

        var data = new BinaryRw(machine.ReadRam(address, size));

        try
        {
            var text = Encoding.UTF8.GetString(data.Data);

            state.Objects.Add(nextId, new TextObject(nextId, size, text));
            state.UsedMemory += size;
            state.OpResult = nextId;
        }
        catch (Exception)
        {
            state.OpResult = (int)GpuError.InvalidObject;
        }
    }

    private static void TryCatchOpCall(Machine machine, GpuDeviceState state, GpuOp op)
    {
        try
        {
            switch (op)
            {
                case GpuOp.Init:
                    OpInit(state);

                    break;
                case GpuOp.GetPixel:
                    OpGetPixel(state);

                    break;
                case GpuOp.SetPainterColor:
                    OpSetPainterColor(state);

                    break;
                case GpuOp.GetPainterColor:
                    OpGetPainterColor(state);

                    break;
                case GpuOp.SetPainterStyle:
                    OpSetPainterStyle(state);

                    break;
                case GpuOp.GetPainterStyle:
                    OpGetPainterStyle(state);

                    break;
                case GpuOp.SetPainterBlendMode:
                    OpSetPainterBlendMode(state);

                    break;
                case GpuOp.GetPainterBlendMode:
                    OpGetPainterBlendMode(state);

                    break;
                case GpuOp.SetPainterFilterQuality:
                    OpSetPainterFilterQuality(state);

                    break;
                case GpuOp.GetPainterFilterQuality:
                    OpGetPainterFilterQuality(state);

                    break;
                case GpuOp.SetPainterHintingLevel:
                    OpSetPainterHintingLevel(state);

                    break;
                case GpuOp.GetPainterHintingLevel:
                    OpGetPainterHintingLevel(state);

                    break;
                case GpuOp.SetPainterAutohinting:
                    OpSetPainterAutohinting(state);

                    break;
                case GpuOp.GetPainterAutohinting:
                    OpGetPainterAutohinting(state);

                    break;
                case GpuOp.SetPainterAntialiasing:
                    OpSetPainterAntialiasing(state);

                    break;
                case GpuOp.GetPainterAntialiasing:
                    OpGetPainterAntialiasing(state);

                    break;
                case GpuOp.SetPainterDithering:
                    OpSetPainterDithering(state);

                    break;
                case GpuOp.GetPainterDithering:
                    OpGetPainterDithering(state);

                    break;
                case GpuOp.MeasureTextWidth:
                    OpMeasureTextWidth(state);

                    break;
                case GpuOp.SetPainterTypeface:
                    OpSetPainterTypeface(state);

                    break;
                case GpuOp.SetPainterTextSize:
                    OpSetPainterTextSize(state);

                    break;
                case GpuOp.GetPainterTextSize:
                    OpGetPainterTextSize(state);

                    break;
                case GpuOp.SetPainterTextScaleX:
                    OpSetPainterTextScaleX(state);

                    break;
                case GpuOp.GetPainterTextScaleX:
                    OpGetPainterTextScaleX(state);

                    break;
                case GpuOp.SetPainterTextSkewX:
                    OpSetPainterTextSkewX(state);

                    break;
                case GpuOp.GetPainterTextSkewX:
                    OpGetPainterTextSkewX(state);

                    break;
                case GpuOp.SetPainterTextAlign:
                    OpSetPainterTextAlign(state);

                    break;
                case GpuOp.GetPainterTextAlign:
                    OpGetPainterTextAlign(state);

                    break;
                case GpuOp.SetPainterSubpixelText:
                    OpSetPainterSubpixelText(state);

                    break;
                case GpuOp.GetPainterSubpixelText:
                    OpGetPainterSubpixelText(state);

                    break;
                case GpuOp.MeasureStringWidth:
                    OpMeasureStringWidth(machine, state);

                    break;
                case GpuOp.GetPainterTypeface:
                    OpGetPainterTypeface(state);

                    break;
                case GpuOp.MeasureStringHeight:
                    OpMeasureStringHeight(machine, state);

                    break;
                case GpuOp.MeasureTextHeight:
                    OpMeasureTextHeight(state);

                    break;
                case GpuOp.CreatePointsObject:
                    OpCreatePointsObject(machine, state);

                    break;
                case GpuOp.DeleteObject:
                    OpDeleteObject(state);

                    break;
                case GpuOp.DeleteAllObjects:
                    OpDeleteAllObjects(state);

                    break;
                case GpuOp.CreateImageObject:
                    OpCreateImageObject(machine, state);

                    break;
                case GpuOp.CreateSurfaceObject:
                    OpCreateSurfaceObject(state);

                    break;
                case GpuOp.SwitchSurface:
                    OpSwitchSurface(state);

                    break;
                case GpuOp.CreateTypefaceObject:
                    OpCreateTypefaceObject(machine, state);

                    break;
                case GpuOp.CreateTextObject:
                    OpCreateTextObject(machine, state);

                    break;
                case GpuOp.DrawPixel:
                    OpDrawPixel(state);

                    break;
                case GpuOp.DrawLine:
                    OpDrawLine(state);

                    break;
                case GpuOp.DrawRect:
                    OpDrawRect(state);

                    break;
                case GpuOp.DrawRoundRect:
                    OpDrawRoundRect(state);

                    break;
                case GpuOp.DrawCircle:
                    OpDrawCircle(state);

                    break;
                case GpuOp.DrawOval:
                    OpDrawOval(state);

                    break;
                case GpuOp.DrawPoints:
                    OpDrawPoints(state);

                    break;
                case GpuOp.DrawText:
                    OpDrawText(state);

                    break;
                case GpuOp.DrawBitmap:
                    OpDrawImage(state);

                    break;
                case GpuOp.DrawBitmapRect:
                    OpDrawImageRect(state);

                    break;
                case GpuOp.DrawBitmapRectSrc:
                    OpDrawImageRectSrc(state);

                    break;
                case GpuOp.DrawString:
                    OpDrawString(machine, state);

                    break;
            }
        }
        catch (Exception)
        {
            state.OpResult = (int)GpuError.Unknown;
        }
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, GpuDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= GpuDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - GpuDeviceComponent.ArgumentsOffset, 0,
                GpuDeviceComponent.Arguments - 1);

            data.Write(state.Arguments[argIndex]);
            return true;
        }

        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.OpResult:
                data.Write(state.OpResult);
                state.OpResult = 0;

                break;
            case DeviceReadRegister.Width:
                data.Write(state.Width);

                break;
            case DeviceReadRegister.Height:
                data.Write(state.Height);

                break;
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, GpuDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= GpuDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - GpuDeviceComponent.ArgumentsOffset, 0,
                GpuDeviceComponent.Arguments - 1);

            state.Arguments[argIndex] = data.ReadDouble();
            return true;
        }

        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.CallOp:
                TryCatchOpCall(machine, state, (GpuOp)data.ReadUInt());

                break;
            case DeviceWriteRegister.Flush:
            {
                if (!data.ReadBool())
                    return true;

                if (state.Framebuffer is null)
                    return true;

                using var snapshot = state.Framebuffer.Snapshot();
                using var ctx = new ZStdCompressionContext();
                var pixels = snapshot.PeekPixels().GetPixelSpan();

                var buf = ArrayPool<byte>.Shared.Rent(ZStd.CompressBound(pixels.Length));
                var length = ctx.Compress2(buf, pixels);

                state.CompressedBuffer = new CompressedBuffer(buf.AsSpan(0, length).ToArray(), pixels.Length);
                state.IsFramebufferDirty = true;

                ArrayPool<byte>.Shared.Return(buf);

                break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister
    {
        OpResult = 0x0,
        Width = 0x1,
        Height = 0x2
    }

    private enum DeviceWriteRegister
    {
        CallOp = 0x0,
        Flush = 0x1
    }

    private enum GpuOp
    {
        Init = 0x0,

        // Pixel ops
        GetPixel = 0x10,

        // Painter ops
        SetPainterColor = 0x100,
        GetPainterColor = 0x101,
        SetPainterStyle = 0x102,
        GetPainterStyle = 0x103,
        SetPainterBlendMode = 0x104,
        GetPainterBlendMode = 0x105,
        SetPainterFilterQuality = 0x106,
        GetPainterFilterQuality = 0x107,
        SetPainterHintingLevel = 0x108,
        GetPainterHintingLevel = 0x109,
        SetPainterAutohinting = 0x10A,
        GetPainterAutohinting = 0x10B,
        SetPainterAntialiasing = 0x10C,
        GetPainterAntialiasing = 0x10D,
        SetPainterDithering = 0x10E,
        GetPainterDithering = 0x10F,
        MeasureTextWidth = 0x110,
        SetPainterTypeface = 0x111,
        SetPainterTextSize = 0x112,
        GetPainterTextSize = 0x113,
        SetPainterTextScaleX = 0x114,
        GetPainterTextScaleX = 0x115,
        SetPainterTextSkewX = 0x116,
        GetPainterTextSkewX = 0x117,
        SetPainterTextAlign = 0x118,
        GetPainterTextAlign = 0x119,
        SetPainterSubpixelText = 0x11A,
        GetPainterSubpixelText = 0x11B,
        MeasureStringWidth = 0x11C,
        GetPainterTypeface = 0x11D,
        MeasureStringHeight = 0x11E,
        MeasureTextHeight = 0x11F,

        // Object ops
        CreatePointsObject = 0x500,
        DeleteObject = 0x501,
        DeleteAllObjects = 0x502,
        CreateImageObject = 0x503,
        CreateSurfaceObject = 0x504,
        SwitchSurface = 0x505,
        CreateTypefaceObject = 0x506,
        CreateTextObject = 0x507,

        // Drawing ops
        DrawPixel = 0x1000,
        DrawLine = 0x1001,
        DrawRect = 0x1002,
        DrawRoundRect = 0x1003,
        DrawCircle = 0x1004,
        DrawOval = 0x1005,
        DrawPoints = 0x1006,
        DrawText = 0x1007,
        DrawBitmap = 0x1008,
        DrawBitmapRect = 0x1009,
        DrawBitmapRectSrc = 0x100A,
        DrawString = 0x100B
    }
}
