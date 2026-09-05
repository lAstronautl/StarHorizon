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
using Content.Shared.ModularComputer.Cpu;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.ModularComputer.Cpu;

[RegisterComponent]
[Access(typeof(CpuSystem))]
public sealed partial class CpuComponent : Component
{
    [ViewVariables] public MachineConfig Config = MachineConfig.Default();

    [ViewVariables] public float DrawRate = 1f;

    [ViewVariables] public Machine? Machine;

    [ViewVariables] public List<MmioDevice> MmioDevices = new();

    /// <summary>
    ///     Initialized from <see cref="Prototype" />
    /// </summary>
    [ViewVariables] public string Name = "";

    [DataField("prototype", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<CpuPrototype>))]
    [ViewVariables]
    public string Prototype = default!;
}
