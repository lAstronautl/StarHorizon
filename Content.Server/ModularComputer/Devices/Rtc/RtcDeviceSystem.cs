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
using Content.Shared.UniverseClock;

namespace Content.Server.ModularComputer.Devices.Rtc;

public sealed class RtcDeviceSystem : MmioDeviceSystem<RtcDeviceComponent, RtcDeviceState>
{
    [Dependency] private readonly CpuSystem _cpu = default!;

    protected override void OnDeviceEvent(EntityUid uid, RtcDeviceComponent component, DeviceEvent ev)
    {
        if (ev is ScheduleInterruptEvent scheduleInterruptEvent)
            component.ScheduledInterrupt = scheduleInterruptEvent.Target;
    }

    protected override void OnUpdate(float frameTime, EntityUid uid, RtcDeviceComponent component)
    {
        if (component.Motherboard is not { } motherboard)
            return;

        if (component.ScheduledInterrupt is not { } deadline)
            return;

        if (SharedUniverseClockSystem.UniversalDateTimeOffset < deadline)
            return;

        component.ScheduledInterrupt = null;
        _cpu.Interrupt(motherboard, null, InterruptMask.MachineTimer);
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, RtcDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.Time:
            {
                var millis = SharedUniverseClockSystem.UniversalDateTimeOffset.ToUnixTimeMilliseconds();

                data.Write(millis);

                break;
            }
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, RtcDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.ScheduleInterrupt:
            {
                var millis = data.ReadULong();
                var deadline = DateTimeOffset.FromUnixTimeMilliseconds((long)millis);

                if (deadline < SharedUniverseClockSystem.UniversalDateTimeOffset)
                {
                    machine.ClearInterrupt(InterruptMask.MachineTimer);
                    break;
                }

                state.TryEnqueueEvent(new ScheduleInterruptEvent(deadline));

                break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        Time = 0x0
    }

    private enum DeviceWriteRegister : byte
    {
        ScheduleInterrupt = 0x0
    }

    private sealed class ScheduleInterruptEvent : DeviceEvent
    {
        public readonly DateTimeOffset Target;

        public ScheduleInterruptEvent(DateTimeOffset target)
        {
            Target = target;
        }
    }
}
