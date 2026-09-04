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
using Content.Shared.ModularComputer.Devices.Screen;

namespace Content.Server.ModularComputer.Devices.Keyboard;

[RegisterComponent]
[Access(typeof(KeyboardDeviceSystem))]
public sealed partial class KeyboardDeviceComponent : PciDeviceComponent<KeyboardDeviceState>
{
    public const int KeysOffset = 0x100;

    [ViewVariables] public bool SendEvents;

    public override PciDevice Device { get; } = new("keyboard", 0x1000, VendorId.VirtTech, DeviceId.Keyboard, false);
}

[Access(typeof(KeyboardDeviceSystem))]
public sealed class KeyboardDeviceState : DeviceState
{
    [ViewVariables] public readonly KeyState[] KeyStates = new KeyState[100];

    [ViewVariables] public bool EventsEnabled;

    [ViewVariables] public KeyboardKey LastChangedKey = KeyboardKey.Unknown;
}

public enum KeyboardKey : byte
{
    A = 0,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Num0,
    Num1,
    Num2,
    Num3,
    Num4,
    Num5,
    Num6,
    Num7,
    Num8,
    Num9,
    NumpadNum0,
    NumpadNum1,
    NumpadNum2,
    NumpadNum3,
    NumpadNum4,
    NumpadNum5,
    NumpadNum6,
    NumpadNum7,
    NumpadNum8,
    NumpadNum9,
    Escape,
    Control,
    Shift,
    Alt,
    LSystem,
    RSystem,
    Menu,
    LBracket,
    RBracket,
    SemiColon,
    Comma,
    Period,
    Apostrophe,
    Slash,
    BackSlash,
    Tilde,
    Equal,
    Space,
    Return,
    NumpadEnter,
    BackSpace,
    Tab,
    PageUp,
    PageDown,
    End,
    Home,
    Insert,
    Delete,
    Minus,
    NumpadAdd,
    NumpadSubtract,
    NumpadDivide,
    NumpadMultiply,
    NumpadDecimal,
    Left,
    Right,
    Up,
    Down,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    Pause,
    World1,
    Unknown = 0xFF
}
