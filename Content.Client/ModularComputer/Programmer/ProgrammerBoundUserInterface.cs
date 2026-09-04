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

using System.Threading.Tasks;
using Content.Shared.ModularComputer.Programmer;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client.ModularComputer.Programmer;

[UsedImplicitly]
public sealed class ProgrammerBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IEntityManager _entity = default!;

    private ProgrammerBoundUserInterfaceState? _lastState;
    private bool _awaitingLoading;
    private readonly ProgrammerWindow _window = new();
    
    public ProgrammerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
        
        _window.OnClose += Close;
        _window.UploadBootromPressed += OnUploadBootromPressed;
    }

    private void OnUploadBootromPressed()
    {
        if (_lastState is null)
            return;
        
        if (_awaitingLoading)
            return;

        _window.UpdateState(new ProgrammerBoundUserInterfaceState(_lastState.EntityUid, ProgrammerState.Loading, _lastState.Limit));
        _window.SetButtonDisabledState(true);
        _awaitingLoading = true;
        
        Task.Run(async () =>
        {
            var programmer = _entity.System<ProgrammerSystem>();

            await programmer.TryLoadBootromFile(_entity.GetEntity(_lastState.EntityUid), null);
            
            _awaitingLoading = false;
            _window.SetButtonDisabledState(false);
        });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not ProgrammerBoundUserInterfaceState castedState)
            return;

        _lastState = castedState;
        _window.UpdateState(castedState);
    }

    protected override void Open()
    {
        base.Open();
        
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        
        _window.Dispose();
    }
}
