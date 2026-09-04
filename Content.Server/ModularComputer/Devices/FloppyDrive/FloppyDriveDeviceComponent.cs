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

using Content.Server.ModularComputer.Devices.Pci;
using Robust.Shared.Audio;
using Robust.Shared.Containers;

namespace Content.Server.ModularComputer.Devices.FloppyDrive;

[RegisterComponent]
[Access(typeof(FloppyDriveDeviceSystem))]
public sealed partial class FloppyDriveDeviceComponent : PciDeviceComponent<FloppyDriveDeviceState>
{
    [ViewVariables] public const int ArgumentsOffset = 0x100;

    [ViewVariables] public const int Arguments = 10;

    [ViewVariables] public const int MaxReadWriteSize = 65536; // 64 KiB

    [DataField("accessSounds", true)] public SoundSpecifier? AccessSounds;

    [DataField("ejectSound", true)] public SoundSpecifier? EjectSound;

    [ViewVariables] public Container FloppyContainer = default!;

    [DataField("floppyContainerId", true, required: true)]
    public string FloppyContainerId = default!;

    [DataField("insertSound", true)] public SoundSpecifier? InsertSound;

    public override PciDevice Device { get; } = new("floppy_drive", 0x1000, VendorId.Emsd, DeviceId.FloppyDrive);
}

[Access(typeof(FloppyDriveDeviceSystem))]
public sealed class FloppyDriveDeviceState : DeviceState
{
    [ViewVariables] public readonly double[] Arguments = new double[FloppyDriveDeviceComponent.Arguments];

    [ViewVariables] public VirtualDisk? Disk;

    [ViewVariables] public double OpResult = (double)FloppyDriveError.Ok;
}

public enum FloppyDriveError
{
    Ok = 0,
    InvalidAddress = -1,
    InvalidSize = -2,
    FloppyDriveIsEmpty = -3,
    Unknown = 0xFFFFFF
}
