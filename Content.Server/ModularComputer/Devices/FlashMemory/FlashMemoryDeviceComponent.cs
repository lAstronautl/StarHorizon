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

namespace Content.Server.ModularComputer.Devices.FlashMemory;

[RegisterComponent]
[Access(typeof(FlashMemoryDeviceSystem))]
public sealed partial class FlashMemoryDeviceComponent : MmioDeviceComponent<FlashMemoryDeviceState>
{
    [ViewVariables] public const int MaxMemorySize = 524288;

    [DataField("preload", true)] public ResPath? Preload;

    public override MmioDevice Device { get; } =
        new("flash_memory", MachineConfig.DefaultMemBase - MaxMemorySize, MaxMemorySize);
}

[Access(typeof(FlashMemoryDeviceSystem))]
public sealed class FlashMemoryDeviceState : DeviceState
{
    [ViewVariables] public VirtualDisk? Disk;
}
