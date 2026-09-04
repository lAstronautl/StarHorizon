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
using Robust.Shared.Prototypes;

namespace Content.Server.ModularComputer.Cpu;

[Prototype("cpu")]
public sealed class CpuPrototype : IPrototype
{
    [DataField("drawRate", true)] public float DrawRate = 1f;

    [DataField("flashMemory", true, required: true)]
    public int FlashMemorySize;

    /// <summary>
    ///     See <see cref="MachineConfig.IPQ" />.
    /// </summary>
    [DataField("ipq", true, required: true)]
    public int Ipq;

    /// <summary>
    ///     Amount of memory, in bytes.
    /// </summary>
    [DataField("memory", true, required: true)]
    public ulong Memory;

    [DataField("name", true, required: true)]
    public string Name = default!;

    [IdDataField] public string ID { get; } = default!;
}
