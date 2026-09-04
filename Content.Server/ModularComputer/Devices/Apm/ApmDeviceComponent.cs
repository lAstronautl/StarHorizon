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

namespace Content.Server.ModularComputer.Devices.Apm;

[RegisterComponent]
[Access(typeof(ApmDeviceSystem))]
public sealed partial class ApmDeviceComponent : MmioDeviceComponent<ApmDeviceState>
{
    public const long Address = 0x2000;

    [ViewVariables] public TimeSpan NextUpdate;

    [ViewVariables] public TimeSpan? ScheduledPowerOnAfterReboot;

    public override MmioDevice Device { get; } = new("apm", Address, 0x3);
}

[Access(typeof(ApmDeviceSystem))]
public sealed class ApmDeviceState : DeviceState
{
    public int Capacity;

    public int Charge;

    public bool HasBattery;
}
