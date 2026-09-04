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

using System.Numerics;
using Content.Server.ModularComputer.Devices.Pci;
using Content.Shared.ModularComputer.Devices.Screen;

namespace Content.Server.ModularComputer.Devices.Mouse;

[RegisterComponent]
[Access(typeof(MouseDeviceSystem))]
public sealed partial class MouseDeviceComponent : PciDeviceComponent<MouseDeviceState>
{
    public const int KeysOffset = 0x100;

    [ViewVariables] public bool SendEvents;

    public override PciDevice Device { get; } = new("mouse", 0x1000, VendorId.VirtTech, DeviceId.Mouse, false);
}

[Access(typeof(MouseDeviceSystem))]
public sealed class MouseDeviceState : DeviceState
{
    [ViewVariables] public readonly KeyState[] KeyStates = new KeyState[3];

    [ViewVariables] public bool EventsEnabled;

    [ViewVariables] public MouseKey LastChangedKey = MouseKey.Unknown;

    [ViewVariables] public LastKeyEventType LastEventType = LastKeyEventType.None;

    [ViewVariables] public Vector2 Position = Vector2.Zero;
}

public enum MouseKey : byte
{
    MouseLeft = 0,
    MouseRight = 1,
    MouseMiddle = 2,
    Unknown = 0xFF
}
