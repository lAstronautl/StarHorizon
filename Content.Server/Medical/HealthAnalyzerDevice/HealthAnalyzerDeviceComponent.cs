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

using Content.Server.ModularComputer.Devices;
using Content.Server.ModularComputer.Devices.Pci;
using Robust.Shared.Audio;

namespace Content.Server.Medical.HealthAnalyzerDevice;

[RegisterComponent]
[Access(typeof(HealthAnalyzerDeviceSystem))]
public sealed partial class HealthAnalyzerDeviceComponent : PciDeviceComponent<HealthAnalyzerDeviceState>
{
    public const int DamageOffset = 0x10;
    
    public override PciDevice Device { get; } = new("health_analyzer", 0x100, VendorId.MedUnion, DeviceId.HealthAnalyzer);
    
    /// <summary>
    /// How long it takes to scan someone.
    /// </summary>
    [DataField("scanDelay")]
    public float ScanDelay = 0.8f;

    /// <summary>
    ///     Sound played on scanning begin
    /// </summary>
    [DataField("scanningBeginSound")]
    public SoundSpecifier? ScanningBeginSound;

    /// <summary>
    ///     Sound played on scanning end
    /// </summary>
    [DataField("scanningEndSound")]
    public SoundSpecifier? ScanningEndSound;
}

[Access(typeof(HealthAnalyzerDeviceSystem))]
public sealed class HealthAnalyzerDeviceState : DeviceState
{
    [ViewVariables]
    public Dictionary<DamageTypeId, float> Damage = new();

    // StarHorizon has no disease system to source this from; the firmware's MMIO protocol still
    // expects the register, so it's kept and always reads back false.
    [ViewVariables]
    public bool HasDisease;
}

public enum DamageTypeId : byte
{
    Asphyxiation = 0,
    Bloodloss = 1,
    Blunt = 2,
    Cellular = 3,
    Caustic = 4,
    Cold = 5,
    Heat = 6,
    Piercing = 7,
    Poison = 8,
    Radiation = 9,
    Shock = 10,
    Slash = 11
}
