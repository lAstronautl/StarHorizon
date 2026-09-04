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

public sealed class PciDevice
{
    [ViewVariables] public readonly DeviceId DeviceId;

    [ViewVariables] public readonly MmioDevice MmioDevice;

    [ViewVariables] public readonly bool ShowInExamine;

    [ViewVariables] public readonly VendorId VendorId;

    [ViewVariables] public byte IrqPin;

    [ViewVariables] public EntityUid Owner;

    [ViewVariables] public Guid Uuid = Guid.Empty;

    public PciDevice(string label, ulong size, VendorId vendorId, DeviceId deviceId, bool showInExamine = true)
    {
        MmioDevice = new MmioDevice(label, 0, size);
        VendorId = vendorId;
        DeviceId = deviceId;
        ShowInExamine = showInExamine;
    }
}

public enum DeviceId : ushort
{
    Tts = 0x64,
    SerialTerminal = 0x65,
    Gpu = 0x66,
    Screen = 0x67,
    Mouse = 0x68,
    Keyboard = 0x69,
    HealthAnalyzer = 0x6A,
    NetworkHub = 0x6B,
    HardDrive = 0x6C,
    FloppyDrive = 0x6D
}

public enum VendorId : ushort
{
    VirtTech = 0x8086,
    HyperVision = 0x043E,
    AdvancedVideoDevices = 0x0955,
    MedUnion = 0x065F,
    CommSolutions = 0x0333,
    Emsd = 0x553F
}
