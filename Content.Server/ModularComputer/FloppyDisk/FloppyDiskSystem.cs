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

using System.Diagnostics.CodeAnalysis;
using Content.Server.ModularComputer.Devices;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.FloppyDisk;

public sealed class FloppyDiskSystem : EntitySystem
{
    private VirtualDisksManager _virtualDisks = default!;

    public override void Initialize()
    {
        _virtualDisks = new VirtualDisksManager("floppy", "floppy", Log);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<FloppyDiskComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<FloppyDiskComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _virtualDisks.DeleteAll();
        _virtualDisks.CreateRoot();
    }

    private void OnComponentShutdown(EntityUid uid, FloppyDiskComponent component, ComponentShutdown args)
    {
        if (component.Disk is null)
            return;

        _virtualDisks.DeleteDisk(component.Disk);
        component.Disk = null;
    }

    private void OnComponentInit(EntityUid uid, FloppyDiskComponent component, ComponentInit args)
    {
        if (component.Preload is not null)
            component.Disk = _virtualDisks.CreateDiskFromCopy(component.Preload.Value, null);
        else
            component.Disk = _virtualDisks.CreateDisk(component.Size);
    }

    [PublicAPI]
    public bool TryDumpFloppyDisk(EntityUid uid, FloppyDiskComponent? component, [NotNullWhen(true)] out ResPath? path)
    {
        path = null;

        if (!Resolve(uid, ref component))
            return false;

        if (component.Disk is null)
            return false;

        path = _virtualDisks.TryDumpDisk(component.Disk, "floppy");

        return true;
    }
}
