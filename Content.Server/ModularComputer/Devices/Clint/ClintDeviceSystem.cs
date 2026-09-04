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
using Content.Server.NTVM;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.Clint;

public sealed class ClintDeviceSystem : MmioDeviceSystem<ClintDeviceComponent, ClintDeviceState>
{
    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, ClintDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.Timer:
                data.Write(machine.GetTimer());

                break;
            case DeviceReadRegister.TimeCmp:
                data.Write(machine.GetTimeCmp());

                break;
            default:
                data.Write(0);

                break;
        }

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, ClintDeviceState state,
        BinaryRw data, int offset)
    {
        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.TimeCmp:
            {
                machine.SetTimeCmp(data.ReadULong());

                break;
            }
            case DeviceWriteRegister.MSoftwareInterrupt:
            {
                var hart = machine.GetHart(0)!;

                DebugTools.AssertNotNull(hart);

                if (data.ReadBool())
                    hart.Interrupt(InterruptMask.MachineSoftware);
                else
                    hart.ClearInterrupt(InterruptMask.MachineSoftware);

                break;
            }
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        Timer = 0x0,
        TimeCmp = 0x1
    }

    private enum DeviceWriteRegister : byte
    {
        TimeCmp = 0x0,
        MSoftwareInterrupt = 0x1
    }
}
