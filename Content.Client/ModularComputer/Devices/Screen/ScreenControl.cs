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

using Content.Client.MainMenu.UI;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.ModularComputer.Devices.Screen;

public sealed class ScreenControl : ShaderedTextureRectControl
{
    [Dependency]
    private readonly IInputManager _input = default!;

    [Dependency]
    private readonly IGameTiming _gameTiming = default!;
    
    private TimeSpan _nextMouseMoveEvent = TimeSpan.Zero;

    public Action<KeyEventArgs, KeyEventType>? KeyEvent;
    public Action<GUIMouseEventArgs>? MouseEvent;

    public ScreenControl()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        KeyboardFocusOnClick = true;
        CanKeyboardFocus = true;

        _input.FirstChanceOnKeyEvent += OnFirstChangeOnKey;
    }

    ~ScreenControl()
    {
        _input.FirstChanceOnKeyEvent -= OnFirstChangeOnKey;
    }

    private void OnFirstChangeOnKey(KeyEventArgs keyevent, KeyEventType type)
    {
        if (!HasKeyboardFocus())
            return;

        KeyEvent?.Invoke(keyevent, type);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        if (_gameTiming.CurTime < _nextMouseMoveEvent)
            return;

        _nextMouseMoveEvent = _gameTiming.CurTime + TimeSpan.FromMilliseconds(10);
        MouseEvent?.Invoke(args);
    }

    protected override void KeyboardFocusEntered()
    {
        Root?.Window?.TextInputStart();
    }

    protected override void KeyboardFocusExited()
    {
        Root?.Window?.TextInputStop();
    }

    protected override void ControlFocusExited()
    {
        Root?.Window?.TextInputStop();
    }
}
