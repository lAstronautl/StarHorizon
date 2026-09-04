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
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Prometheus;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.FlashMemory;

public sealed class FlashMemoryDeviceSystem : MmioDeviceSystem<FlashMemoryDeviceComponent, FlashMemoryDeviceState>
{
    private static readonly Gauge FlashMemoryDiskLogicMemoryUsage = Metrics.CreateGauge(
        "machines_flash_memory_disk_logic_memory_usage", "Disk logic memory used by machines flash memory (in bytes)");

    private static readonly Gauge FlashMemoryDiskPhysMemoryUsage = Metrics.CreateGauge(
        "machines_flash_memory_disk_phys_memory_usage", "Disk phys memory used by machines flash memory (in bytes)");

    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private TimeSpan _nextMetricsUpdate = TimeSpan.Zero;

    private VirtualDisksManager _virtualDisks = default!;

    public override void Initialize()
    {
        base.Initialize();

        _virtualDisks = new VirtualDisksManager("flash", "flash", Log);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextMetricsUpdate)
            return;

        _nextMetricsUpdate = _gameTiming.CurTime + TimeSpan.FromSeconds(10);

        var logicMemoryUsed = 0;
        var physMemoryUsed = 0;
        var query = EntityQueryEnumerator<FlashMemoryDeviceComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            UpdateState(uid, component, state =>
            {
                if (state.Disk is null)
                    return;

                logicMemoryUsed += state.Disk.Size;
                physMemoryUsed += state.Disk.PhysSize;
            });
        }

        FlashMemoryDiskLogicMemoryUsage.Set(logicMemoryUsed);
        FlashMemoryDiskPhysMemoryUsage.Set(physMemoryUsed);
    }

    [PublicAPI]
    public void InitFlashMemory(EntityUid uid, FlashMemoryDeviceComponent? component, int size)
    {
        if (!Resolve(uid, ref component))
            return;

        Log.Debug($"Initializing flash memory with size 0x{size:x8} for {ToPrettyString(uid)}");

        DebugTools.Assert(size <= FlashMemoryDeviceComponent.MaxMemorySize);
        DebugTools.Assert(size > 0);
        DebugTools.Assert(size % 2 == 0);

        UpdateState(uid, component, state =>
        {
            state.Disk = _virtualDisks.CreateDisk(size);
            state.Disk.Write(BitConverter.GetBytes(size), 0);
        });
    }

    protected override void OnComponentStartup(EntityUid uid, FlashMemoryDeviceComponent component,
        ComponentStartup args)
    {
        base.OnComponentStartup(uid, component, args);

        UpdateState(uid, component, state =>
        {
            if (component.Preload is not null)
                state.Disk = _virtualDisks.CreateDiskFromCopy(component.Preload.Value, null);
        });
    }

    protected override void OnComponentShutdown(EntityUid uid, FlashMemoryDeviceComponent component,
        ComponentShutdown args)
    {
        UpdateState(uid, component, state =>
        {
            if (state.Disk is null)
                return;

            _virtualDisks.DeleteDisk(state.Disk);
            state.Disk = null;
        });

        base.OnComponentShutdown(uid, component, args);
    }

    [PublicAPI]
    public bool TryDumpFlashMemory(EntityUid uid, FlashMemoryDeviceComponent? component,
        [NotNullWhen(true)] out ResPath? path)
    {
        path = null;

        if (!Resolve(uid, ref component))
            return false;

        path = UpdateState<ResPath?>(uid, component, state =>
        {
            if (state.Disk is null)
                return null;

            return _virtualDisks.TryDumpDisk(state.Disk, "flash");
        });

        return path is not null;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _virtualDisks.DeleteAll();
        _virtualDisks.CreateRoot();
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, FlashMemoryDeviceState state,
        BinaryRw data, int offset)
    {
        if (state.Disk is null)
            return false;

        if (offset > state.Disk.Size || offset + data.Data.Length > state.Disk.Size)
            return false;

        state.Disk.Read(data.Data, offset);

        return true;
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, FlashMemoryDeviceState state,
        BinaryRw data, int offset)
    {
        if (state.Disk is null)
            return false;

        if (offset > state.Disk.Size || offset + data.Data.Length > state.Disk.Size)
            return false;

        state.Disk.Write(data.Data, offset);

        return true;
    }
}
