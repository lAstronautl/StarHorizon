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

namespace Content.Server.ModularComputer.Devices.Pci;

[RegisterComponent]
[Access(typeof(PciBusDeviceSystem))]
public sealed partial class PciBusDeviceComponent : MmioDeviceComponent<PciBusDeviceState>
{
    [ViewVariables] public const int UuidRegisterOffset = 0x2;

    [ViewVariables] public const int UuidLength = 16;

    [ViewVariables] public const int MaxDevices = 32;

    [ViewVariables] public const ulong Address = 0x0FF80000;

    [ViewVariables] public const ulong Size = 0xFFFF;

    public override MmioDevice Device { get; } = new("pci_bus", Address, Size);
}

[Access(typeof(PciBusDeviceSystem))]
public sealed class PciBusDeviceState : DeviceState
{
    [ViewVariables] public readonly byte[] Irq = new byte[PciBusDeviceComponent.MaxDevices];

    [ViewVariables] public ulong MemoryAddress = PciBusDeviceComponent.Address + PciBusDeviceComponent.Size;

    [ViewVariables] public List<PciDevice> Devices { get; } = new(PciBusDeviceComponent.MaxDevices);
}
