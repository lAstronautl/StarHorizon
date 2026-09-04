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

namespace Content.Server.ModularComputer.Devices.Rtc;

[RegisterComponent]
[Access(typeof(RtcDeviceSystem))]
public sealed partial class RtcDeviceComponent : MmioDeviceComponent<RtcDeviceState>
{
    public const ulong Address = 0x1000;

    [ViewVariables] public DateTimeOffset? ScheduledInterrupt;

    public override MmioDevice Device { get; } = new("rtc", Address, 0x8);
}

[Access(typeof(RtcDeviceSystem))]
public sealed class RtcDeviceState : DeviceState
{
}
