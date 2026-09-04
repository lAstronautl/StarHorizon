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
using Content.Server.ModularComputer.Devices.Mouse;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.ModularComputer.Devices.Plic;
using Content.Server.NTVM;
using Content.Shared.ModularComputer.Devices.Screen;

namespace Content.Server.ModularComputer.Devices.Keyboard;

public sealed class KeyboardDeviceSystem : PciDeviceSystem<KeyboardDeviceComponent, KeyboardDeviceState>
{
    [Dependency] private readonly PlicDeviceSystem _plic = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KeyboardDeviceComponent, ScreenKeyMessage>(OnScreenKeyPressed);
    }

    protected override void OnDeviceEvent(EntityUid uid, KeyboardDeviceComponent component, DeviceEvent ev)
    {
        if (ev is UpdateSendKeyboardEvents keyboardEvent)
        {
            component.SendEvents = keyboardEvent.State;

            UpdateState(uid, component, state => { state.EventsEnabled = component.SendEvents; });
        }
    }

    private void OnScreenKeyPressed(EntityUid uid, KeyboardDeviceComponent component, ScreenKeyMessage args)
    {
        if (component.Motherboard is null)
            return;

        if (!component.SendEvents)
            return;

        if (args.KeyArgs.Key < ScreenKey.A)
            return;

        var keyboardKey = (KeyboardKey)((byte)args.KeyArgs.Key - (byte)ScreenKey.A);

        if (args.State == KeyState.Down)
        {
            var ev = new KeyboardDeviceKeyPressedEvent(uid);
            RaiseLocalEvent(Transform(uid).ParentUid, ref ev);
        }

        UpdateState(uid, component, state =>
        {
            state.KeyStates[(int)keyboardKey] = args.State;
            state.LastChangedKey = keyboardKey;

            _plic.SendIrq(component.Motherboard!.Value, null, null, component.Device.IrqPin);
        });
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, KeyboardDeviceState state,
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
                case DeviceReadRegister.LastKey:
                    data.Write((byte)state.LastChangedKey);
                    state.LastChangedKey = KeyboardKey.Unknown;

                    break;
            }
        }
        else
        {
            var idx = Math.Clamp(offset - KeyboardDeviceComponent.KeysOffset, 0, state.KeyStates.Length - 1);

            data.Write((byte)state.KeyStates[idx]);
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, KeyboardDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset < KeyboardDeviceComponent.KeysOffset)
        {
            var reg = (DeviceWriteRegister)offset;

            switch (reg)
            {
                case DeviceWriteRegister.Events:
                    var newState = data.ReadBool();

                    state.TryEnqueueEvent(new UpdateSendKeyboardEvents(newState));

                    break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        Events = 0,
        LastKey = 1
    }

    private enum DeviceWriteRegister : byte
    {
        Events = 0
    }

    private sealed class UpdateSendKeyboardEvents : DeviceEvent
    {
        public readonly bool State;

        public UpdateSendKeyboardEvents(bool state)
        {
            State = state;
        }
    }
}

[ByRefEvent]
public record struct KeyboardDeviceKeyPressedEvent(EntityUid Keyboard);
