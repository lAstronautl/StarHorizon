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

using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.ModularComputer.Devices.Plic;
using Content.Server.NTVM;
using Content.Shared.ModularComputer.Devices.Screen;

namespace Content.Server.ModularComputer.Devices.Mouse;

public sealed class MouseDeviceSystem : PciDeviceSystem<MouseDeviceComponent, MouseDeviceState>
{
    [Dependency] private readonly PlicDeviceSystem _plic = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MouseDeviceComponent, ScreenKeyMessage>(OnScreenKeyPressed);
        SubscribeLocalEvent<MouseDeviceComponent, MouseMoveMessage>(OnMouseMove);
    }

    protected override void OnDeviceEvent(EntityUid uid, MouseDeviceComponent component, DeviceEvent ev)
    {
        if (ev is UpdateSendMouseEvents mouseEvents)
        {
            component.SendEvents = mouseEvents.State;

            UpdateState(uid, component, state => { state.EventsEnabled = component.SendEvents; });
        }
    }

    private void OnMouseMove(EntityUid uid, MouseDeviceComponent component, MouseMoveMessage args)
    {
        if (component.Motherboard is null)
            return;

        if (!component.SendEvents)
            return;

        UpdateState(uid, component, state =>
        {
            state.LastEventType = LastKeyEventType.Move;
            state.Position = args.Position;

            _plic.SendIrq(component.Motherboard!.Value, null, null, component.Device.IrqPin);
        });
    }

    private void OnScreenKeyPressed(EntityUid uid, MouseDeviceComponent component, ScreenKeyMessage args)
    {
        if (component.Motherboard is null)
            return;

        if (!component.SendEvents)
            return;

        if (args.KeyArgs.Key is < ScreenKey.MouseLeft or > ScreenKey.MouseMiddle)
            return;

        if (args.State == KeyState.Down)
        {
            var ev = new MouseDeviceClickedEvent(uid);
            RaiseLocalEvent(Transform(uid).ParentUid, ref ev);
        }

        var mouseKey = (MouseKey)((byte)args.KeyArgs.Key - (byte)ScreenKey.MouseLeft);

        UpdateState(uid, component, state =>
        {
            state.KeyStates[(int)mouseKey] = args.State;
            state.LastChangedKey = mouseKey;
            state.LastEventType = LastKeyEventType.Key;

            _plic.SendIrq(component.Motherboard!.Value, null, null, component.Device.IrqPin);
        });
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, MouseDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset < MouseDeviceComponent.KeysOffset)
        {
            var reg = (DeviceReadRegister)offset;

            switch (reg)
            {
                case DeviceReadRegister.Events:
                    data.Write(state.EventsEnabled);

                    break;
                case DeviceReadRegister.LastEventType:
                    data.Write((byte)state.LastEventType);
                    state.LastEventType = LastKeyEventType.None;

                    break;
                case DeviceReadRegister.LastKey:
                    data.Write((byte)state.LastChangedKey);
                    state.LastChangedKey = MouseKey.Unknown;

                    break;
                case DeviceReadRegister.Position:
                    var x = (long)state.Position.X;
                    var y = (long)state.Position.Y << 32;

                    data.Write(y | x);

                    break;
            }
        }
        else
        {
            var idx = Math.Clamp(offset - MouseDeviceComponent.KeysOffset, 0, state.KeyStates.Length - 1);

            data.Write((byte)state.KeyStates[idx]);
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, MouseDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset < MouseDeviceComponent.KeysOffset)
        {
            var reg = (DeviceWriteRegister)offset;

            switch (reg)
            {
                case DeviceWriteRegister.Events:
                    var newState = data.ReadBool();

                    state.TryEnqueueEvent(new UpdateSendMouseEvents(newState));

                    break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        Events = 0,
        LastEventType = 1,
        LastKey = 2,
        Position = 3
    }

    private enum DeviceWriteRegister : byte
    {
        Events = 0
    }

    private sealed class UpdateSendMouseEvents : DeviceEvent
    {
        public readonly bool State;

        public UpdateSendMouseEvents(bool state)
        {
            State = state;
        }
    }
}

[ByRefEvent]
public record struct MouseDeviceClickedEvent(EntityUid Mouse);
