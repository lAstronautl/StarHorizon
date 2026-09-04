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

namespace Content.Server.ModularComputer.Devices.Plic;

[RegisterComponent]
[Access(typeof(PlicDeviceSystem))]
public sealed partial class PlicDeviceComponent : MmioDeviceComponent<PlicDeviceState>
{
    public const long Address = 0x5000;
    public const int IrqsOffset = 0x10;

    public override MmioDevice Device { get; } = new("plic", Address, 0x1000);
}

[Access(typeof(PlicDeviceSystem))]
public sealed class PlicDeviceState : DeviceState
{
    public const int SourcesMax = 64;

    [ViewVariables] public readonly Dictionary<byte, Irq> Irqs = new(SourcesMax);

    [ViewVariables] public byte NextIrq = 1;

    [ViewVariables] public byte Threshold = 0;
}

public sealed class Irq
{
    [ViewVariables] public byte Index;

    [ViewVariables] public bool IsEnabled;

    [ViewVariables] public bool IsPending;

    [ViewVariables] public byte Priority;

    public Irq(byte index, byte priority, bool isEnabled, bool isPending)
    {
        Index = index;
        Priority = priority;
        IsEnabled = isEnabled;
        IsPending = isPending;
    }
}
