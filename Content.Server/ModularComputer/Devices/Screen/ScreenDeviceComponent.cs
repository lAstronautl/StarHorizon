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

namespace Content.Server.ModularComputer.Devices.Screen;

[RegisterComponent]
[Access(typeof(ScreenDeviceSystem))]
public sealed partial class ScreenDeviceComponent : PciDeviceComponent<ScreenDeviceState>
{
    public const int MaxFps = 24;

    /// <summary>
    ///     In bytes.
    /// </summary>
    public const int MaxImageSize = 400_000;

    [DataField("borderColor", true)] public Color BorderColor = Color.FromHex("#171615");

    [ViewVariables] public EntityUid? Gpu;

    [DataField("height", required: true, readOnly: true)]
    public int Height;

    [DataField("label", true)] public string Label = "HyperVision";

    [DataField("labelColor", true)] public Color LabelColor = Color.FromHex("#bcbbba");

    [ViewVariables] public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField("width", required: true, readOnly: true)]
    public int Width;

    public override PciDevice Device { get; } = new("screen", 0x100, VendorId.HyperVision, DeviceId.Screen);
}

[Access(typeof(ScreenDeviceSystem))]
public sealed class ScreenDeviceState : DeviceState
{
    [ViewVariables] public int Height;

    [ViewVariables] public bool IsConnected;

    [ViewVariables] public int Width;
}
