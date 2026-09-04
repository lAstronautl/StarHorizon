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

using System.Linq;
using Content.Server.DeviceLinking.Events;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.ModularComputer.Devices.Plic;
using Content.Server.NTVM;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
using SignalReceivedEvent = Content.Server.DeviceLinking.Events.SignalReceivedEvent;

namespace Content.Server.ModularComputer.Devices.NetworkHub;

public sealed class NetworkHubDeviceSystem : PciDeviceSystem<NetworkHubDeviceComponent, NetworkHubDeviceState>
{
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;

    [Dependency] private readonly PlicDeviceSystem _plic = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NetworkHubDeviceComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<NetworkHubDeviceComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<NetworkHubDeviceComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    protected override void OnComponentStartup(EntityUid uid, NetworkHubDeviceComponent component,
        ComponentStartup args)
    {
        base.OnComponentStartup(uid, component, args);

        UpdateState(uid, component, state =>
        {
            var sourceComponent = Comp<DeviceLinkSourceComponent>(uid);
            var sinkComponent = Comp<DeviceLinkSinkComponent>(uid);

            state.InputPorts = sinkComponent.Ports.ToDictionary(k => (string)k, k => new InputHubPort(k));
            state.OutputPorts = sourceComponent.Ports.ToDictionary(k => (string)k, k => new OutputHubPort(k));
        });
    }

    private void OnSignalReceived(EntityUid uid, NetworkHubDeviceComponent component, ref SignalReceivedEvent args)
    {
        if (component.Motherboard is not { } motherboard)
            return;

        var data = args.Data;
        var portId = args.Port;

        UpdateState(uid, component, state =>
        {
            if (!state.InputPorts.TryGetValue(portId, out var inputPort))
                return;

            if (!inputPort.IsEnabled)
                return;

            if (data is not null)
            {
                if (inputPort.Mode != HubPortMode.Complex)
                    return;

                if (inputPort.MessagesQueue.Count == NetworkHubDeviceComponent.MaxPortQuery)
                    return;

                if (!data.TryGetValue(NetworkHubDeviceComponent.MessagePayload, out var payload) ||
                    payload is not byte[] bytePayload)
                    return;

                inputPort.MessagesQueue.Enqueue(bytePayload);
            }

            inputPort.IsPending = true;

            _plic.SendIrq(motherboard, null, null, component.Device.IrqPin);
        });
    }

    private void OnPortDisconnected(EntityUid uid, NetworkHubDeviceComponent component, PortDisconnectedEvent args)
    {
        UpdateState(uid, component, state =>
        {
            if (state.InputPorts.TryGetValue(args.Port, out var inputPort))
            {
                inputPort.IsConnected = false;
                inputPort.IsPending = false;
            }
            else if (state.OutputPorts.TryGetValue(args.Port, out var outputPort))
                outputPort.IsConnected = false;
        });
    }

    private void OnNewLink(EntityUid uid, NetworkHubDeviceComponent component, NewLinkEvent args)
    {
        UpdateState(uid, component, state =>
        {
            if (uid == args.Sink && state.InputPorts.TryGetValue(args.SinkPort, out var inputPort))
                inputPort.IsConnected = true;
            else if (uid == args.Source && state.OutputPorts.TryGetValue(args.SourcePort, out var outputPort))
                outputPort.IsConnected = true;
        });
    }

    protected override void OnAttachedToPciBus(EntityUid uid, NetworkHubDeviceComponent component,
        ref AttachedToPciBusEvent args)
    {
        base.OnAttachedToPciBus(uid, component, ref args);

        UpdateState(uid, component, state =>
        {
            foreach (var inputPort in state.InputPorts.Values)
            {
                inputPort.IsEnabled = false;
                inputPort.IsPending = false;
                inputPort.Mode = HubPortMode.Simple;
            }

            foreach (var outputPort in state.OutputPorts.Values)
            {
                outputPort.Mode = HubPortMode.Simple;
            }
        });
    }

    protected override void OnDeviceEvent(EntityUid uid, NetworkHubDeviceComponent component, DeviceEvent ev)
    {
        if (component.Motherboard is null)
            return;

        if (ev is InvokeSimpleOutputPortEvent simpleInvoke)
            _deviceLink.InvokePort(uid, simpleInvoke.PortId);
        else if (ev is InvokeComplexOutputPortEvent complexInvoke)
        {
            var payload = new NetworkPayload
            {
                [DeviceNetworkConstants.Command] = NetworkHubDeviceComponent.EthernetMessageCommand,
                [NetworkHubDeviceComponent.MessagePayload] = complexInvoke.Data
            };

            _deviceLink.InvokePort(uid, complexInvoke.PortId, payload);
        }
    }

    private static string OutputPortIndexToId(int index)
    {
        return $"{NetworkHubDeviceComponent.BaseOutputName}{index}";
    }

    private static string InputPortIndexToId(int index)
    {
        return $"{NetworkHubDeviceComponent.BaseInputName}{index}";
    }

    private static int OutputPortIdToIndex(string id)
    {
        return int.Parse(id.Split(NetworkHubDeviceComponent.BaseOutputName)[1]);
    }

    private static int InputPortIdToIndex(string id)
    {
        return int.Parse(id.Split(NetworkHubDeviceComponent.BaseInputName)[1]);
    }

    private static void OpSetInputPortStatus(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];
        var newState = (int)args[1] == 1;

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        inputPort.MessagesQueue.Clear();
        inputPort.IsEnabled = newState;
    }

    private static void OpIsInputPortConnected(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.OpResult = inputPort.IsConnected ? 1.0 : 0.0;
    }

    private static void OpIsOutputPortConnected(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or >= NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.OutputPorts.TryGetValue(OutputPortIndexToId(portIndex), out var outputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.OpResult = outputPort.IsConnected ? 1.0 : 0.0;
    }

    private static void OpInvokeSimpleOutputPort(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        var portId = OutputPortIndexToId(portIndex);
        if (!state.OutputPorts.TryGetValue(portId, out var outputPort) || !outputPort.IsConnected)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.TryEnqueueEvent(new InvokeSimpleOutputPortEvent(portId));
        state.OpResult = (double)NetworkHubError.Ok;
    }

    private static void OpIsInputPortPending(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.OpResult = inputPort.IsPending ? 1.0 : 0.0;
    }

    private static void OpClaimSimpleInputPort(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (inputPort.Mode != HubPortMode.Simple)
        {
            state.OpResult = (double)NetworkHubError.InvalidPortMode;
            return;
        }

        inputPort.IsPending = false;
        state.OpResult = (double)NetworkHubError.Ok;
    }

    private static void OpGetInputPortMode(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.OpResult = (double)inputPort.Mode;
    }

    private static void OpGetOutputPortMode(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.OutputPorts.TryGetValue(OutputPortIndexToId(portIndex), out var outputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        state.OpResult = (double)outputPort.Mode;
    }

    private static void OpSetInputPortMode(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];
        var mode = (HubPortMode)args[1];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        inputPort.MessagesQueue.Clear();
        inputPort.Mode = mode;
        state.OpResult = (double)NetworkHubError.Ok;
    }

    private static void OpSetOutputPortMode(NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];
        var mode = (HubPortMode)args[1];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.OutputPorts.TryGetValue(OutputPortIndexToId(portIndex), out var outputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        outputPort.Mode = mode;
        state.OpResult = (double)NetworkHubError.Ok;
    }

    private static void OpInvokeComplexOutputPort(Machine machine, NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];
        var address = (ulong)args[1];
        var size = (int)args[2];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        var portId = OutputPortIndexToId(portIndex);

        if (!state.OutputPorts.TryGetValue(portId, out var outputPort) || !outputPort.IsConnected)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (outputPort.Mode != HubPortMode.Complex)
        {
            state.OpResult = (double)NetworkHubError.InvalidPortMode;
            return;
        }

        if (size > NetworkHubDeviceComponent.MaxMessageSize)
        {
            state.OpResult = (double)NetworkHubError.InvalidSize;
            return;
        }

        var data = machine.ReadRam(address, size);

        state.TryEnqueueEvent(new InvokeComplexOutputPortEvent(portId, data));
        state.OpResult = (double)NetworkHubError.Ok;
    }

    private static void OpGetPendingInputPort(NetworkHubDeviceState state)
    {
        var port = state.InputPorts.Values.FirstOrDefault(port => port.IsPending);

        if (port is null)
        {
            state.OpResult = 0.0;
            return;
        }

        var index = InputPortIdToIndex(port.Id);
        state.OpResult = index;
    }

    private static void OpClaimComplexInputPort(Machine machine, NetworkHubDeviceState state)
    {
        var args = state.Arguments;
        var portIndex = (int)args[0];
        var address = (ulong)args[1];

        if (portIndex is <= 0 or > NetworkHubDeviceComponent.MaxPorts)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (!state.InputPorts.TryGetValue(InputPortIndexToId(portIndex), out var inputPort))
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        if (inputPort.Mode != HubPortMode.Complex)
        {
            state.OpResult = (double)NetworkHubError.InvalidPortMode;
            return;
        }

        if (inputPort.MessagesQueue.Count == 0)
        {
            state.OpResult = (double)NetworkHubError.InvalidPort;
            return;
        }

        var message = inputPort.MessagesQueue.Dequeue();
        machine.WriteRam(message, address);

        if (inputPort.MessagesQueue.Count == 0)
        {
            inputPort.IsPending = false;
            state.OpResult = (double)NetworkHubError.Ok;
        }
    }

    private static void TryCatchOpCall(Machine machine, NetworkHubDeviceState state, NetworkHubOp op)
    {
        try
        {
            switch (op)
            {
                case NetworkHubOp.SetInputPortStatus:
                    OpSetInputPortStatus(state);

                    break;
                case NetworkHubOp.IsInputPortConnected:
                    OpIsInputPortConnected(state);

                    break;
                case NetworkHubOp.IsOutputPortConnected:
                    OpIsOutputPortConnected(state);

                    break;
                case NetworkHubOp.InvokeSimpleOutputPort:
                    OpInvokeSimpleOutputPort(state);

                    break;
                case NetworkHubOp.IsInputPortPending:
                    OpIsInputPortPending(state);

                    break;
                case NetworkHubOp.ClaimSimpleInputPort:
                    OpClaimSimpleInputPort(state);

                    break;
                case NetworkHubOp.GetInputPortMode:
                    OpGetInputPortMode(state);

                    break;
                case NetworkHubOp.GetOutputPortMode:
                    OpGetOutputPortMode(state);

                    break;
                case NetworkHubOp.SetInputPortMode:
                    OpSetInputPortMode(state);

                    break;
                case NetworkHubOp.SetOutputPortMode:
                    OpSetOutputPortMode(state);

                    break;
                case NetworkHubOp.InvokeComplexOutputPort:
                    OpInvokeComplexOutputPort(machine, state);

                    break;
                case NetworkHubOp.GetPendingInputPort:
                    OpGetPendingInputPort(state);

                    break;
                case NetworkHubOp.ClaimComplexInputPort:
                    OpClaimComplexInputPort(machine, state);

                    break;
            }
        }
        catch (Exception)
        {
            state.OpResult = (int)NetworkHubError.Unknown;
        }
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, NetworkHubDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= NetworkHubDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - NetworkHubDeviceComponent.ArgumentsOffset, 0,
                NetworkHubDeviceComponent.Arguments - 1);

            state.Arguments[argIndex] = data.ReadDouble();
            return true;
        }

        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.CallOp:
            {
                TryCatchOpCall(machine, state, (NetworkHubOp)data.ReadUInt());

                break;
            }
        }

        return true;
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, NetworkHubDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= NetworkHubDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - NetworkHubDeviceComponent.ArgumentsOffset, 0,
                NetworkHubDeviceComponent.Arguments - 1);

            state.Arguments[argIndex] = data.ReadDouble();
            return true;
        }

        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.OpResult:
            {
                data.Write(state.OpResult);

                break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        OpResult = 0x0
    }

    private enum DeviceWriteRegister : byte
    {
        CallOp = 0x0
    }

    private enum NetworkHubOp : byte
    {
        SetInputPortStatus = 0x0,
        IsInputPortConnected = 0x1,
        IsOutputPortConnected = 0x2,
        InvokeSimpleOutputPort = 0x3,
        IsInputPortPending = 0x4,
        ClaimSimpleInputPort = 0x5,
        GetInputPortMode = 0x6,
        GetOutputPortMode = 0x7,
        SetInputPortMode = 0x8,
        SetOutputPortMode = 0x9,
        InvokeComplexOutputPort = 0xA,
        GetPendingInputPort = 0xB,
        ClaimComplexInputPort = 0xC
    }

    private sealed class InvokeSimpleOutputPortEvent : DeviceEvent
    {
        public readonly string PortId;

        public InvokeSimpleOutputPortEvent(string portId)
        {
            PortId = portId;
        }
    }

    private sealed class InvokeComplexOutputPortEvent : DeviceEvent
    {
        public readonly byte[] Data;
        public readonly string PortId;

        public InvokeComplexOutputPortEvent(string portId, byte[] data)
        {
            PortId = portId;
            Data = data;
        }
    }
}
