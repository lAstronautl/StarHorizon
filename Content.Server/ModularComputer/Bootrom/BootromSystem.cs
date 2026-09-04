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
using System.IO;
using System.Linq;
using Content.Server.ModularComputer.Devices;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Prometheus;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Bootrom;

public sealed class BootromSystem : EntitySystem
{
    private static readonly Gauge BootromDiskLogicMemoryUsage = Metrics.CreateGauge(
        "machines_bootrom_disk_logic_memory_usage", "Disk logic memory used by machines bootroms (in bytes)");

    private static readonly Gauge BootromDiskPhysMemoryUsage =
        Metrics.CreateGauge("machines_bootrom_disk_phys_memory_usage",
            "Disk phys memory used by machines bootroms (in bytes)");

    [Dependency] private readonly IGameTiming _gameTiming = default!;

    [Dependency] private readonly IResourceManager _resource = default!;

    private TimeSpan _nextMetricsUpdate = TimeSpan.Zero;
    private TimeSpan _nextUpdate = TimeSpan.Zero;

    private VirtualDisksManager _virtualDisks = default!;

    public override void Initialize()
    {
        base.Initialize();

        _virtualDisks = new VirtualDisksManager("bootroms", "boot", Log);

        SubscribeLocalEvent<BootromComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnComponentInit(EntityUid uid, BootromComponent component, ComponentInit args)
    {
        if (component.Preload is null)
            return;

        component.Disk = _virtualDisks.CreateDiskFromCopy(component.Preload.Value, null);
    }

    public override void Update(float frameTime)
    {
        MetricsUpdate();
        GcUpdate();
    }

    private void GcUpdate()
    {
        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + TimeSpan.FromSeconds(30);

        var files = Directory.GetFiles(_virtualDisks.GetRootPath()).ToHashSet();
        var usedFiles = new HashSet<VirtualDisk>();

        var query = EntityQueryEnumerator<BootromComponent>();

        while (query.MoveNext(out _, out var component))
        {
            if (component.Disk is null)
                continue;

            usedFiles.Add(component.Disk);
        }

        foreach (var file in files)
        {
            if (usedFiles.Any(d => d.Path == file))
                continue;

            try
            {
                Log.Debug($"Deleting unused file `{file}`");
                File.Delete(file);
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
        }
    }

    private void MetricsUpdate()
    {
        if (_gameTiming.CurTime < _nextMetricsUpdate)
            return;

        _nextMetricsUpdate = _gameTiming.CurTime + TimeSpan.FromSeconds(10);

        var logicMemoryUsed = 0;
        var physMemoryUsed = 0;
        var query = EntityQueryEnumerator<BootromComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Disk is null)
                return;

            logicMemoryUsed += component.Disk.Size;
            physMemoryUsed += component.Disk.PhysSize;
        }

        BootromDiskLogicMemoryUsage.Set(logicMemoryUsed);
        BootromDiskPhysMemoryUsage.Set(physMemoryUsed);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _virtualDisks.DeleteAll();
        _virtualDisks.CreateRoot();
    }

    private void UpdateDisk(EntityUid uid, BootromComponent? component, VirtualDisk? disk)
    {
        if (!Resolve(uid, ref component))
            return;

        var ev = new BootromPreUpdateEvent();
        RaiseLocalEvent(uid, ref ev);

        component.Disk = disk;
    }

    [PublicAPI]
    public void CopyFrom(EntityUid target, BootromComponent? targetComponent, EntityUid from,
        BootromComponent? fromComponent)
    {
        if (!Resolve(target, ref targetComponent))
            return;

        if (!Resolve(from, ref fromComponent))
            return;

        UpdateDisk(target, targetComponent, fromComponent.Disk);
    }

    [PublicAPI]
    public bool TryLoadFromStream(EntityUid uid, BootromComponent? component, Stream stream,
        [NotNullWhen(true)] out VirtualDisk? disk)
    {
        disk = null;

        if (!Resolve(uid, ref component))
            return false;

        if (stream.Length >= BootromComponent.MaxFileSize)
            return false;

        disk = _virtualDisks.CreateDiskFromStream(stream);
        UpdateDisk(uid, EnsureComp<BootromComponent>(uid), disk);

        return true;
    }

    [PublicAPI]
    public bool TryLoadFromResources(EntityUid uid, BootromComponent? component, ResPath path,
        [NotNullWhen(true)] out VirtualDisk? disk)
    {
        disk = null;

        if (!Resolve(uid, ref component))
            return false;

        var stream = _resource.ContentFileRead(path);

        if (!TryLoadFromStream(uid, component, stream, out disk))
            return false;

        UpdateDisk(uid, EnsureComp<BootromComponent>(uid), disk);

        return true;
    }
}

[ByRefEvent]
public record struct BootromPreUpdateEvent;
