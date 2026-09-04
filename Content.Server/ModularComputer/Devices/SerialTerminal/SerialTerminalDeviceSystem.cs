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

using System.Text;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.NTVM;
using Content.Shared.ModularComputer.Devices.SerialTerminal;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.SerialTerminal;

public sealed class
    SerialTerminalDeviceSystem : PciDeviceSystem<SerialTerminalDeviceComponent, SerialTerminalDeviceState>
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SerialTerminalDeviceComponent, SerialTerminalSendTextMessage>(OnSerialTerminalSendText);
    }

    private void OnSerialTerminalSendText(EntityUid uid, SerialTerminalDeviceComponent component,
        SerialTerminalSendTextMessage args)
    {
        if (component.Motherboard is null)
            return;

        SendString(uid, component, args.Message);
        UpdateUiState(uid, component);
    }

    protected override void OnDeviceEvent(EntityUid uid, SerialTerminalDeviceComponent component, DeviceEvent ev)
    {
        if (component.Motherboard is null)
            return;

        if (ev is DataIncomingEvent dataEvent)
        {
            if (string.IsNullOrEmpty(dataEvent.Data))
                return;

            component.Content.Add(dataEvent.Data);

            UpdateUiState(uid, component);
        }
    }

    private void UpdateUiState(EntityUid uid, SerialTerminalDeviceComponent component)
    {
        if (!_ui.HasUi(uid, SerialTerminalUiKey.Key))
            return;

        var state = new SerialTerminalBoundUserInterfaceState(component.Content);
        _ui.SetUiState(uid, SerialTerminalUiKey.Key, state);
    }

    [PublicAPI]
    public void SendString(EntityUid uid, SerialTerminalDeviceComponent? component, string text)
    {
        if (!Resolve(uid, ref component))
            return;

        UpdateState(uid, component, state =>
        {
            if (state.InBuffer.Count == SerialTerminalDeviceComponent.BufferSize)
                return;

            var data = Encoding.UTF8.GetBytes(text);
            var length = Math.Min(SerialTerminalDeviceComponent.BufferSize - 1, data.Length);

            component.Content.Add(">>> " + text);

            if (component.Content.Count > SerialTerminalDeviceComponent.MaxLines)
                component.Content.Pop();

            for (var i = 0; i < length; i++)
            {
                state.InBuffer.Enqueue(data[i]);
            }

            state.InBuffer.Enqueue(0);
        });
    }

    protected override void OnDetachedFromPciBus(EntityUid uid, SerialTerminalDeviceComponent component,
        ref DetachedFromPciBusEvent args)
    {
        component.Content.Clear();

        base.OnDetachedFromPciBus(uid, component, ref args);
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device,
        SerialTerminalDeviceState state, BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.ReadInBuffer:
                if (state.InBuffer.TryDequeue(out var ch))
                {
                    data.Write(ch, offset);

                    return true;
                }

                break;
            case DeviceReadRegister.ReadInBufferLength:
                data.Write(state.InBuffer.Count);

                break;
            case DeviceReadRegister.ReadOutBufferLength:
                data.Write(state.OutBuffer.Count);

                break;
        }

        data.Write(0);

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device,
        SerialTerminalDeviceState state, BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;
        var ch = data.Data[0];

        switch (register)
        {
            case DeviceWriteRegister.WriteOutBuffer:
                if (ch == '\0' || state.OutBuffer.Count == SerialTerminalDeviceComponent.BufferSize)
                {
                    var bytes = new byte[state.OutBuffer.Count];
                    var i = 0;

                    while (state.OutBuffer.TryDequeue(out var b))
                    {
                        bytes[i] = b;
                        i += 1;
                    }

                    var str = Encoding.UTF8.GetString(bytes);
                    state.TryEnqueueEvent(new DataIncomingEvent(str));
                    state.OutBuffer.Clear();

                    if (ch != '\0')
                        state.OutBuffer.Enqueue(ch);
                }
                else
                    state.OutBuffer.Enqueue(ch);

                break;
        }

        return true;
    }

    private sealed class DataIncomingEvent : DeviceEvent
    {
        public readonly string Data;

        public DataIncomingEvent(string data)
        {
            Data = data;
        }
    }

    private enum DeviceReadRegister : byte
    {
        ReadInBuffer = 0x0,
        ReadInBufferLength = 0x4,
        ReadOutBufferLength = 0x8
    }

    private enum DeviceWriteRegister : byte
    {
        WriteOutBuffer = 0x0
    }
}
