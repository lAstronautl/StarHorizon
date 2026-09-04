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
using Content.Server.ModularComputer.Cpu;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.Plic;

public sealed class PlicDeviceSystem : MmioDeviceSystem<PlicDeviceComponent, PlicDeviceState>
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlicDeviceComponent, MachineStartedEvent>(OnMachineStarted);
    }

    private void OnMachineStarted(EntityUid uid, PlicDeviceComponent component, ref MachineStartedEvent args)
    {
        DebugTools.AssertNotNull(component.Motherboard);

        UpdateState(uid, component, state =>
        {
            state.NextIrq = 0;
            state.Irqs.Clear();
            state.Threshold = 0;
        });
    }

    private static void ClaimIrq(Machine machine, PlicDeviceState state, Irq irq)
    {
        irq.IsPending = false;

        if (FindPendingIrq(state) is null)
            machine.ClearInterrupt(InterruptMask.MachineExternal);
    }

    private static void InterruptIrq(Machine machine, PlicDeviceState state, Irq irq)
    {
        if (!irq.IsEnabled)
            return;

        if (irq.Priority <= state.Threshold)
            return;

        machine.Interrupt(InterruptMask.MachineExternal);
    }

    private static Irq? FindPendingIrq(PlicDeviceState state)
    {
        var irq = state.Irqs.Values
            .Where(irq => irq is { IsEnabled: true, IsPending: true } && irq.Priority > state.Threshold)
            .MaxBy(irq => irq.Priority);

        return irq;
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, PlicDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset < PlicDeviceComponent.IrqsOffset)
        {
            var reg = (DeviceReadRegister)offset;

            switch (reg)
            {
                case DeviceReadRegister.Threshold:
                    data.Write(state.Threshold);

                    break;
                case DeviceReadRegister.Pending:
                    var pending = FindPendingIrq(state);

                    if (pending is not null)
                        data.Write(pending.Index);
                    else
                        data.Write(0);

                    break;
            }
        }
        else
        {
            var irqIdx = Math.Clamp(offset - PlicDeviceComponent.IrqsOffset, 0, PlicDeviceState.SourcesMax - 1);

            if (irqIdx == 0 || !state.Irqs.TryGetValue((byte)irqIdx, out var irq))
                return true;

            // Returns information about IRQ:
            //
            // 16  10          9           8          0
            // +---+-----------+-----------+----------+
            // | - | IsPending | IsEnabled | Priority |
            // +---+-----------+-----------+----------+
            //   6       1           1          8
            //

            var pendingBits = ((irq.IsPending ? 1 : 0) & 1) << 9;
            var enabledBits = ((irq.IsEnabled ? 1 : 0) & 1) << 8;
            var priorityBits = irq.Priority;
            var info = pendingBits | enabledBits | priorityBits;

            data.Write((short)info);
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, PlicDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= PlicDeviceComponent.IrqsOffset)
            return true;

        var reg = (DeviceWriteRegister)offset;

        switch (reg)
        {
            case DeviceWriteRegister.Threshold:
                state.Threshold = data.ReadByte();

                break;
            case DeviceWriteRegister.IrqInfo:
                // Sets IRQ state:
                // 
                // 32  24      16     8     0
                // +---+-------+------+-----+
                // | - | Value | Type | Irq |
                // +---+-------+------+-----+
                //   8     8      8      8
                //
                var d = data.ReadInt();
                var irqIdx = (byte)(d & 0xFF);
                var type = (IrqValueType)((d >> 8) & 0xFF);
                var value = (byte)((d >> 16) & 0xFF);

                if (irqIdx == 0 || !state.Irqs.TryGetValue(irqIdx, out var irq))
                    return true;

                switch (type)
                {
                    case IrqValueType.Priority:
                        irq.Priority = value;

                        break;
                    case IrqValueType.IsEnabled:
                        irq.IsEnabled = value != 0;

                        break;
                    case IrqValueType.Claim:
                        ClaimIrq(machine, state, irq);

                        break;
                }

                break;
        }

        return true;
    }

    [PublicAPI]
    public byte AllocIrq(EntityUid uid, PlicDeviceComponent? component)
    {
        if (!Resolve(uid, ref component))
            return 0;

        return UpdateState(uid, component, state =>
        {
            if (state.NextIrq >= PlicDeviceState.SourcesMax)
            {
                Log.Error($"{ToPrettyString(uid)} out of irqs!");

                return (byte)0;
            }

            var ret = state.NextIrq;

            state.Irqs.Add(ret, new Irq(ret, 0, false, false));
            state.NextIrq += 1;

            return ret;
        });
    }

    [PublicAPI]
    public void SendIrq(EntityUid uid, PlicDeviceComponent? component, CpuComponent? motherboardComponent, byte irqIdx)
    {
        if (!Resolve(uid, ref component, ref motherboardComponent))
            return;

        if (motherboardComponent.Machine is null)
            return;

        if (irqIdx == 0)
            return;

        UpdateState(uid, component, state =>
        {
            if (!state.Irqs.TryGetValue(irqIdx, out var irq))
                return;

            if (!irq.IsEnabled || irq.Priority <= state.Threshold)
                return;

            irq.IsPending = true;
            InterruptIrq(motherboardComponent.Machine, state, irq);
        });
    }

    private enum DeviceReadRegister : byte
    {
        Threshold = 0,
        Pending
    }

    private enum DeviceWriteRegister : byte
    {
        Threshold = 0,
        IrqInfo
    }

    private enum IrqValueType : byte
    {
        Priority = 0,
        IsEnabled,
        Claim
    }
}
