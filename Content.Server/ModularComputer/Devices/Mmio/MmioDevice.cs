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

using Content.Server.NTVM;
using JetBrains.Annotations;

namespace Content.Server.ModularComputer.Devices.Mmio;

public sealed class MmioDevice
{
    [PublicAPI] [ViewVariables] public ulong Address;

    [PublicAPI] [ViewVariables] public string Label;

    [PublicAPI] public Func<Machine, MmioDevice, BinaryRw, int, bool>? MmioRead;

    [PublicAPI] public Func<Machine, MmioDevice, BinaryRw, int, bool>? MmioWrite;

    [PublicAPI] [ViewVariables] public ulong Size;

    public MmioDevice(string label, ulong address, ulong size)
    {
        Label = label;
        Address = address;
        Size = size;
    }
}
