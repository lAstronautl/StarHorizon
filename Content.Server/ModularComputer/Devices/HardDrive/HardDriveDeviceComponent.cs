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
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.HardDrive;

[RegisterComponent]
[Access(typeof(HardDriveDeviceSystem))]
public sealed partial class HardDriveDeviceComponent : PciDeviceComponent<HardDriveDeviceState>
{
    [ViewVariables] public const int ArgumentsOffset = 0x100;

    [ViewVariables] public const int Arguments = 10;

    [ViewVariables] public const int MaxReadWriteSize = 65536; // 64 KiB

    [DataField("accessSounds", true)] public SoundSpecifier? AccessSounds;

    [ViewVariables] public TimeSpan NextSound = TimeSpan.Zero;

    [DataField("preload", true)] public ResPath? Preload;

    [DataField("size", true, required: true)]
    public int Size;

    public override PciDevice Device { get; } = new("hard_drive", 0x1000, VendorId.Emsd, DeviceId.HardDrive);
}

[Access(typeof(HardDriveDeviceSystem))]
public sealed class HardDriveDeviceState : DeviceState
{
    [ViewVariables] public readonly double[] Arguments = new double[HardDriveDeviceComponent.Arguments];

    [ViewVariables] public VirtualDisk? Disk;

    [ViewVariables] public double OpResult = (double)HardDriveError.Ok;
}

public enum HardDriveError
{
    Ok = 0,
    InvalidAddress = -1,
    InvalidSize = -2,
    Unknown = 0xFFFFFF
}
