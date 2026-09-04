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

using Content.Server.Administration;
using Content.Server.ModularComputer.Cpu;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.ModularComputer;

[AdminCommand(AdminFlags.Admin)]
public sealed class ShutdownComputersCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entity = default!;

    public string Command => "shutdowncomps";
    public string Description => "Shutdowns all the modular computers";
    public string Help => "Usage: shutdowncomps";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var motherboardSystem = _entity.System<CpuSystem>();

        var query = _entity.EntityQueryEnumerator<CpuComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            motherboardSystem.TryTurnOff(uid, component);
        }
    }
}
