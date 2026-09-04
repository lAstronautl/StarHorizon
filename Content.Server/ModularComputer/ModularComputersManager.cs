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

using System.Threading;
using Content.Server.NTVM;

namespace Content.Server.ModularComputer;

public sealed class ModularComputersManager
{
    [Dependency] private readonly ILogManager _log = default!;

    private Thread? _eventLoop;

    private ISawmill _sawmill = default!;

    public void Initialize()
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("comps_ev_loop");
    }

    /// <summary>
    ///     Starts an event loop in separate thread.
    /// </summary>
    /// <returns>false - if already running, otherwise true.</returns>
    public bool TryStartEventLoop()
    {
        if (_eventLoop is { IsAlive: true })
            return false;

        _sawmill.Info("Starting event loop");

        _eventLoop = new Thread(() =>
        {
            Machine.RunEventLoop();
            _sawmill.Info("Event loop is over");
        });

        _eventLoop.Start();

        return true;
    }

    public bool IsEventLoopRunning()
    {
        return _eventLoop is { IsAlive: true };
    }
}
