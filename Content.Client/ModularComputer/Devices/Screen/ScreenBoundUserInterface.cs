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

using Content.Shared.ModularComputer.Devices.Screen;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.UserInterface;

namespace Content.Client.ModularComputer.Devices.Screen;

[UsedImplicitly]
public sealed class ScreenBoundUserInterface : BoundUserInterface
{
    private readonly ScreenWindow _window = new();

    private bool _sendMouseEvents;
    private bool _sendKeyboardEvents;

    public ScreenBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _window.KeyEvent += OnKeyEvent;
        _window.MouseEvent += OnMouseEvent;
        _window.OnClose += Close;
    }

    private void OnMouseEvent(GUIMouseEventArgs args)
    {
        if (!_sendMouseEvents)
            return;
        
        SendMessage(new MouseMoveMessage(args.RelativePosition));
    }

    private void OnKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (!_sendMouseEvents && !_sendKeyboardEvents)
            return;
        
        var keyState = type switch
        {
            KeyEventType.Down => KeyState.Down,
            KeyEventType.Repeat => KeyState.Repeat,
            _ => KeyState.Up
        };

        SendMessage(new ScreenKeyMessage(new KeyArgs((ScreenKey)args.Key, args.IsRepeat, args.Alt, args.Control, args.Shift,
            args.System, args.ScanCode), keyState));
    }

    protected override void Open()
    {
        base.Open();

        _window.Open();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not ScreenBoundUserInterfaceState castedState)
            return;

        _sendMouseEvents = castedState.SendMouseEvents;
        _sendKeyboardEvents = castedState.SendKeyboardEvents;
        _window.UpdateState(castedState);
    }
    
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        
        if (!disposing)
            return;
        
        _window.Dispose();
    }
}
