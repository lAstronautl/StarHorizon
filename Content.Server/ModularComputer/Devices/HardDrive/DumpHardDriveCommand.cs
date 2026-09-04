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

using Robust.Shared.Console;

namespace Content.Server.ModularComputer.Devices.HardDrive;

public sealed class DumpHardDriveCommand : IConsoleCommand
{
    public string Command => "dump_hdd";
    public string Description => "Save a HDD file in server's data/ folder";
    public string Help => $"Usage: {Command} <entityUid>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(
                $"Received invalid amount of arguments arguments. Expected 1, got {args.Length}.\nUsage: {Help}");
            return;
        }

        var entityManager = IoCManager.Resolve<IEntityManager>();

        if (!EntityUid.TryParse(args[0], out var uid) || !entityManager.EntityExists(uid))
        {
            shell.WriteError($"No entity found with uid {uid}");
            return;
        }

        if (!entityManager.TryGetComponent(uid, out HardDriveDeviceComponent? hddComponent))
        {
            shell.WriteError($"Entity {uid} has no {nameof(HardDriveDeviceComponent)} component");
            return;
        }

        var hddSystem = entityManager.System<HardDriveDeviceSystem>();

        if (hddSystem.TryDumpHdd(uid, hddComponent, out var path))
            shell.WriteLine($"HDD dumped to {path}");
        else
            shell.WriteError("Can't dump the HDD!");
    }
}
