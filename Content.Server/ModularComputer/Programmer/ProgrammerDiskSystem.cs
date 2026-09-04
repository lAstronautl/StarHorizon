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

using Content.Server.ModularComputer.Bootrom;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Content.Shared.ModularComputer.Programmer;

namespace Content.Server.ModularComputer.Programmer;

public sealed class ProgrammerDiskSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;

    [Dependency] private readonly ProgrammerSystem _programmer = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ProgrammerDiskComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ProgrammerDiskComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnComponentInit(EntityUid uid, ProgrammerDiskComponent component, ComponentInit args)
    {
        EnsureComp<BootromComponent>(uid);
    }

    private void OnAfterInteract(EntityUid uid, ProgrammerDiskComponent component, AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<ProgrammerComponent>(args.Target))
            return;

        var bootromComponent = Comp<BootromComponent>(uid);

        if (bootromComponent.Disk is null)
            return;

        _popup.PopupEntity(Loc.GetString("programmer-disk-bootrom-copied"), args.Target.Value);
        _programmer.LoadBootrom(args.Target.Value, null, uid, bootromComponent);

        args.Handled = true;
    }
}
