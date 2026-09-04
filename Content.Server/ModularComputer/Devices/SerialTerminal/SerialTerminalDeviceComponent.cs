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

namespace Content.Server.ModularComputer.Devices.SerialTerminal;

[RegisterComponent]
[Access(typeof(SerialTerminalDeviceSystem))]
public sealed partial class SerialTerminalDeviceComponent : PciDeviceComponent<SerialTerminalDeviceState>
{
    public const int BufferSize = 1024;
    public const int MaxLines = 100;

    [ViewVariables] public readonly List<string> Content = new();

    public override PciDevice Device { get; } = new("serial_device", 0x8, VendorId.VirtTech, DeviceId.SerialTerminal);
}

[Access(typeof(SerialTerminalDeviceSystem))]
public sealed class SerialTerminalDeviceState : DeviceState
{
    [ViewVariables] public readonly Queue<byte> InBuffer = new();

    [ViewVariables] public readonly Queue<byte> OutBuffer = new();
}
