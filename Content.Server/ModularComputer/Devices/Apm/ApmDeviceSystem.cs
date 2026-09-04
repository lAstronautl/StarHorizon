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
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using Content.Server.PowerCell;
using Robust.Shared.Timing;

namespace Content.Server.ModularComputer.Devices.Apm;

public sealed class ApmDeviceSystem : MmioDeviceSystem<ApmDeviceComponent, ApmDeviceState>
{
    [Dependency] private readonly CpuSystem _cpu = default!;

    [Dependency] private readonly PowerCellSystem _powerCell = default!;

    [Dependency] private readonly IGameTiming _timing = default!;

    protected override void OnDeviceEvent(EntityUid uid, ApmDeviceComponent component, DeviceEvent ev)
    {
        switch (ev)
        {
            case MachineShutdownEvent:
                _cpu.TryTurnOff(uid, null);

                break;
            case MachineRebootEvent:
                component.ScheduledPowerOnAfterReboot = _timing.CurTime + TimeSpan.FromSeconds(3);
                _cpu.TryTurnOff(uid, null);

                break;
        }
    }

    protected override void OnUpdate(float frameTime, EntityUid uid, ApmDeviceComponent component)
    {
        var xFormQuery = GetEntityQuery<TransformComponent>();

        if (_timing.CurTime > component.ScheduledPowerOnAfterReboot)
        {
            _cpu.TryStart(uid, null);
            component.ScheduledPowerOnAfterReboot = null;
        }

        if (_timing.CurTime < component.NextUpdate)
            return;

        component.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(5);

        var xForm = xFormQuery.GetComponent(uid);

        if (_powerCell.TryGetBatteryFromSlot(xForm.ParentUid, out var battery))
        {
            UpdateState(uid, component, state =>
            {
                state.HasBattery = true;
                state.Capacity = (int)battery.MaxCharge;
                state.Charge = (int)battery.CurrentCharge;
            });
        }
        else
        {
            UpdateState(uid, component, state =>
            {
                state.HasBattery = false;
                state.Capacity = 0;
                state.Charge = 0;
            });
        }
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, ApmDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.HasBattery:
                data.Write(state.HasBattery);

                break;
            case DeviceReadRegister.Capacity:
                data.Write(state.Capacity);

                break;
            case DeviceReadRegister.Charge:
                data.Write(state.Charge);

                break;
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, ApmDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.Shutdown:
                state.TryEnqueueEvent(new MachineShutdownEvent());

                break;
            case DeviceWriteRegister.Reboot:
                state.TryEnqueueEvent(new MachineRebootEvent());

                break;
        }

        return true;
    }

    private sealed class MachineShutdownEvent : DeviceEvent
    {
    }

    private sealed class MachineRebootEvent : DeviceEvent
    {
    }

    private enum DeviceReadRegister : byte
    {
        HasBattery = 0x0,
        Capacity = 0x1,
        Charge = 0x2
    }

    private enum DeviceWriteRegister : byte
    {
        Shutdown = 0x0,
        Reboot = 0x1
    }
}
