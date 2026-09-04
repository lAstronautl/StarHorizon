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

namespace Content.Server.ModularComputer.Devices.Clint;

[RegisterComponent]
[Access(typeof(ClintDeviceSystem))]
public sealed partial class ClintDeviceComponent : MmioDeviceComponent<ClintDeviceState>
{
    public const int Address = 0x3000;
    public const int Size = 0x1000;

    public override MmioDevice Device { get; } = new("clint",
        Address, Size);
}

[Access(typeof(ClintDeviceSystem))]
public sealed class ClintDeviceState : DeviceState
{
}
