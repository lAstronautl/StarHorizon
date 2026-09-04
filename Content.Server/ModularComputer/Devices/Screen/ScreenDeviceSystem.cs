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

using Content.Server.ModularComputer.Cpu;
using Content.Server.ModularComputer.Devices.Gpu;
using Content.Server.ModularComputer.Devices.Keyboard;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Mouse;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.NTVM;
using Content.Shared.Examine;
using Content.Shared.ModularComputer.Devices.Screen;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.Screen;

public sealed class ScreenDeviceSystem : PciDeviceSystem<ScreenDeviceComponent, ScreenDeviceState>
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    [Dependency] private readonly GpuDeviceSystem _gpu = default!;

    [Dependency] private readonly PciBusDeviceSystem _pciBus = default!;

    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScreenDeviceComponent, MachineTurnedOffEvent>(OnMachineTurnedOff);
        SubscribeLocalEvent<ScreenDeviceComponent, BoundUIOpenedEvent>(OnBoundUiOpened);
    }

    protected override void OnExamined(EntityUid uid, ScreenDeviceComponent component, ExaminedEvent args)
    {
        base.OnExamined(uid, component, args);

        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("modular-computers-screen-resolution-examined", ("width", component.Width),
            ("height", component.Height)));
    }

    private void OnBoundUiOpened(EntityUid uid, ScreenDeviceComponent component, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, component, true);
    }

    private void OnMachineTurnedOff(EntityUid uid, ScreenDeviceComponent component, ref MachineTurnedOffEvent args)
    {
        Disconnect(uid, component);
        UpdateUiState(uid, component);
    }

    protected override void OnDetachedFromPciBus(EntityUid uid, ScreenDeviceComponent component,
        ref DetachedFromPciBusEvent args)
    {
        Disconnect(uid, component);
        UpdateUiState(uid, component);

        base.OnDetachedFromPciBus(uid, component, ref args);
    }

    protected override void OnComponentStartup(EntityUid uid, ScreenDeviceComponent component, ComponentStartup args)
    {
        base.OnComponentStartup(uid, component, args);

        UpdateState(uid, component, state =>
        {
            state.Width = component.Width;
            state.Height = component.Height;
        });
    }

    protected override void OnUpdate(float frameTime, EntityUid uid, ScreenDeviceComponent component)
    {
        if (_gameTiming.CurTime < component.NextUpdate)
            return;

        UpdateUiState(uid, component);
    }

    private void UpdateUiState(EntityUid uid, ScreenDeviceComponent component, bool force = false)
    {
        if (!_ui.HasUi(uid, ScreenUiKey.Key))
            return;

        var state = new ScreenBoundUserInterfaceState(component.Width, component.Height, null, false, false,
            component.BorderColor, component.LabelColor, component.Label);

        if (component.Gpu is not { } gpu)
            goto defer;

        if (!gpu.Valid)
        {
            component.Gpu = null;
            goto defer;
        }

        if (!TryComp(gpu, out GpuDeviceComponent? gpuComponent))
        {
            component.Gpu = null;
            goto defer;
        }

        if (!force && !_gpu.IsFramebufferDirty(gpu, gpuComponent))
            return;

        var framebuffer = _gpu.GetFramebufferAndMarkClean(gpu, gpuComponent);

        if (framebuffer.IsEmpty())
            return;

        state.Framebuffer = framebuffer;
        var maxPossibleFps = ScreenDeviceComponent.MaxImageSize / framebuffer.Data.Length;

        component.NextUpdate = _gameTiming.CurTime +
                               TimeSpan.FromSeconds(1) / Math.Min(maxPossibleFps, ScreenDeviceComponent.MaxFps);

        defer:

        if (TryComp(uid, out MouseDeviceComponent? mouseDeviceComponent))
            state.SendMouseEvents = mouseDeviceComponent.SendEvents;

        if (TryComp(uid, out KeyboardDeviceComponent? keyboardDeviceComponent))
            state.SendKeyboardEvents = keyboardDeviceComponent.SendEvents;

        _ui.SetUiState(uid, ScreenUiKey.Key, state);
    }

    protected override void OnDeviceEvent(EntityUid uid, ScreenDeviceComponent component, DeviceEvent ev)
    {
        if (component.Motherboard is not { } motherboard)
            return;

        if (ev is ConnectScreenEvent connectEv)
        {
            EntityUid? gpu = null;

            foreach (var device in _pciBus.GetDevices(motherboard, null))
            {
                if (device.MmioDevice.Address != connectEv.Address)
                    continue;

                gpu = device.Owner;
                break;
            }

            UpdateState(uid, component, state => { state.IsConnected = gpu is not null; });

            if (gpu is null)
                return;

            DebugTools.Assert(gpu.Value.Valid);

            component.Gpu = gpu;
        }
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, ScreenDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.Width:
                data.Write(state.Width);

                break;
            case DeviceReadRegister.Height:
                data.Write(state.Height);

                break;
            case DeviceReadRegister.IsConnected:
                data.Write(state.IsConnected);

                break;
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, ScreenDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.GpuAddress:
                state.TryEnqueueEvent(new ConnectScreenEvent(data.ReadULong()));

                break;
        }

        return true;
    }

    private void Disconnect(EntityUid uid, ScreenDeviceComponent component)
    {
        component.Gpu = null;

        UpdateState(uid, component, state => state.IsConnected = false);
    }

    private sealed class ConnectScreenEvent : DeviceEvent
    {
        public readonly ulong Address;

        public ConnectScreenEvent(ulong address)
        {
            Address = address;
        }
    }

    private enum DeviceReadRegister : byte
    {
        Width = 0x0,
        Height = 0x4,
        IsConnected = 0x8
    }

    private enum DeviceWriteRegister : byte
    {
        GpuAddress = 0x0
    }
}
