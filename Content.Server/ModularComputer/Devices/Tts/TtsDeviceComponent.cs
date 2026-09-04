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

using System.Text;
using Content.Server.ModularComputer.Devices.Pci;

namespace Content.Server.ModularComputer.Devices.Tts;

[RegisterComponent]
[Access(typeof(TtsDeviceSystem))]
public sealed partial class TtsDeviceComponent : PciDeviceComponent<TtsDeviceState>
{
    public TimeSpan NextSpeech = TimeSpan.Zero;
    public override PciDevice Device { get; } = new("tts", 0xC, VendorId.VirtTech, DeviceId.Tts);
}

[Access(typeof(TtsDeviceSystem))]
public sealed class TtsDeviceState : DeviceState
{
    [ViewVariables] public bool IsReady = true;

    [ViewVariables] public StringBuilder String = new();
}
