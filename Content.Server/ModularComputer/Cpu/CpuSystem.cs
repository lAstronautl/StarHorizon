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

using Content.Server.Audio;
using Content.Server.ModularComputer.Bootrom;
using Content.Server.ModularComputer.Devices.FlashMemory;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using Content.Server.Popups;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Prometheus;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Cpu;

public sealed partial class CpuSystem : EntitySystem
{
    private static readonly Gauge MachinesCountMetric =
        Metrics.CreateGauge("machines_count", "Number of machines on the server.");

    private static readonly Gauge RunningMachinesCountMetric =
        Metrics.CreateGauge("running_machines_count", "Number of running machines");

    private static readonly Gauge MachinesMemoryUsage =
        Metrics.CreateGauge("machines_memory_usage", "Memory used by machines (in bytes)");

    [Dependency] private readonly AmbientSoundSystem _ambientSound = default!;

    [Dependency] private readonly AudioSystem _audio = default!;

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    [Dependency] private readonly ModularComputersManager _computers = default!;

    [Dependency] private readonly FlashMemoryDeviceSystem _flashMemory = default!;

    [Dependency] private readonly PopupSystem _popup = default!;

    [Dependency] private readonly IPrototypeManager _prototype = default!;

    [Dependency] private readonly IGameTiming _timing = default!;

    private bool _enabled;
    private int _maxMachines;
    private ulong _maxMemory;
    private ulong _memoryUsed;
    private TimeSpan _nextUpdate = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(CCVars.ModularComputersEnabled, OnModularComputersEnabledChanged, true);
        _cfg.OnValueChanged(CCVars.ModularComputersMaxMachinesHard, newValue => _maxMachines = newValue, true);
        _cfg.OnValueChanged(CCVars.ModularComputersMaxMemory, newValue => _maxMemory = (ulong)newValue, true);

        DebugTools.Assert(_maxMachines >= 0);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeLocalEvent<CpuComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<CpuComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<CpuComponent, ExaminedEvent>(OnExaminedEvent);
        SubscribeLocalEvent<CpuComponent, BootromPreUpdateEvent>(OnBootromPreUpdate);
    }

    private void OnBootromPreUpdate(EntityUid uid, CpuComponent component, ref BootromPreUpdateEvent args)
    {
        if (component.Machine is null)
            return;

        TryTurnOff(uid, component);
    }

    private void OnComponentShutdown(EntityUid uid, CpuComponent component, ComponentShutdown args)
    {
        TryTurnOff(uid, component);

        var toDetach = new ValueList<MmioDevice>(component.MmioDevices);

        foreach (var device in toDetach)
        {
            TryDetachMmioDevice(uid, component, device);
        }

        _memoryUsed -= component.Config.MemSize;
        component.Machine?.Dispose();
        component.Machine = null;
    }

    [PublicAPI]
    public bool TryTurnOff(EntityUid uid, CpuComponent? component)
    {
        if (component is null && !Resolve(uid, ref component))
            return false;

        if (component.Machine is not { } machine)
            return false;

        if (!machine.IsPowered())
            return false;

        Log.Info($"Turning off machine on motherboard `{ToPrettyString(uid)}`");
        machine.Shutdown();

        _popup.PopupEntity(Loc.GetString("modular-computers-cpu-shutdowns"), uid);
        _ambientSound.SetAmbience(uid, false);

        var xForm = Transform(uid);
        var ev = new MachineTurnedOffEvent(uid);

        RaiseLocalEvent(uid, ref ev);
        RaiseLocalEvent(xForm.ParentUid, ref ev);

        return true;
    }

    [PublicAPI]
    public bool IsPowered(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return false;

        return component.Machine?.IsPowered() ?? false;
    }

    [PublicAPI]
    public bool IsRunning(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return false;

        return component.Machine?.IsRunning() ?? false;
    }

    [PublicAPI]
    public int GetHartsCount(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return 0;

        return (int)(component.Machine?.GetHartsCount() ?? 0);
    }

    [PublicAPI]
    public List<Hart> GetHarts(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return new List<Hart>();

        if (component.Machine is not { } machine)
            return new List<Hart>();

        var harts = new List<Hart>();

        for (ulong i = 0; i < machine.GetHartsCount(); i++)
        {
            if (machine.GetHart(i) is not { } hart)
                break;

            harts.Add(hart);
        }

        return harts;
    }

    [PublicAPI]
    public void TogglePower(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Machine?.IsPowered() ?? false)
            TryTurnOff(uid, component);
        else
            TryStart(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        if (_timing.CurTime <= _nextUpdate)
            return;
        if (!_enabled)
            return;

        MachinesCountMetric.Set(Machine.MachinesCount());
        MachinesMemoryUsage.Set(_memoryUsed);

        var runningMachines = 0;
        var machinesQuery = EntityQueryEnumerator<CpuComponent>();

        while (machinesQuery.MoveNext(out var uid, out var component))
        {
            if (IsRunning(uid, component))
                runningMachines += 1;
        }

        RunningMachinesCountMetric.Set(runningMachines);

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(10);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        if (!_enabled)
            return;

        var query = EntityQueryEnumerator<CpuComponent>();

        Log.Info("Disposing motherboards and machines");

        while (query.MoveNext(out var uid, out var motherboardComponent))
        {
            if (motherboardComponent.Machine is not { } machine)
                continue;

            if (!machine.IsPowered())
                continue;

            Log.Info($"Shutdown motherboard `{ToPrettyString(uid)}`");

            machine.Shutdown();
            machine.Dispose();

            motherboardComponent.Machine = null;
        }

        _memoryUsed = 0;

        DebugTools.Assert(Machine.MachinesCount() == 0);
        DebugTools.Assert(_computers.IsEventLoopRunning() == false);
    }

    [PublicAPI]
    public bool TryStart(EntityUid uid, CpuComponent? component)
    {
        if (!_enabled)
            return false;

        if (!Resolve(uid, ref component))
            return false;

        if (component.Machine is not null)
        {
            if (component.Machine.IsPowered())
                return true;

            // Clean previous state
            component.Machine.Dispose();
            component.Machine = null;
        }

        // Motherboard requires a hull for functioning.
        // By now only the parent is considered to be used as a hull, so
        // the motherboard should lie inside the hull entity.
        var xForm = Transform(uid);

        if (!xForm.ParentUid.Valid)
            return false;

        var startAttemptEv = new MachineStartAttempt(uid, false);

        RaiseLocalEvent(xForm.ParentUid, ref startAttemptEv);

        if (startAttemptEv.Cancelled)
            return false;

        component.Machine = new Machine(component.Config);
        component.Machine.MMIOAccess +=
            (_, address, data, access) => OnMmioAccess(uid, component, address, data, access);

        if (!component.Machine.IsPowered())
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Signals/ping3.ogg"), uid);

        Log.Info($"Staring machine on motherboard `{ToPrettyString(uid)}`");

        if (TryComp<BootromComponent>(uid, out var bootromComponent) && bootromComponent.Disk is not null)
            component.Machine.LoadBootrom(bootromComponent.Disk.Path);
        else
            component.Machine.LoadBootrom(null);

        var startedEv = new MachineStartedEvent(uid);

        RaiseLocalEvent(uid, ref startedEv);
        RaiseLocalEvent(xForm.ParentUid, ref startedEv);

        component.Machine.Run();

        _ambientSound.SetAmbience(uid, true);
        _computers.TryStartEventLoop();

        return true;
    }

    [PublicAPI]
    public void Interrupt(EntityUid uid, CpuComponent? component, InterruptMask interrupt)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Machine?.Interrupt(interrupt);
    }

    [PublicAPI]
    public void ClearInterrupt(EntityUid uid, CpuComponent? component, InterruptMask interrupt)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Machine?.ClearInterrupt(interrupt);
    }

    private void OnModularComputersEnabledChanged(bool newValue)
    {
        if (_enabled == newValue)
            return;

        _enabled = newValue;

        if (_enabled)
            return;

        Log.Warning("Modular computers disabled! Turning off and disposing motherboards...");

        var query = EntityQueryEnumerator<CpuComponent>();

        while (query.MoveNext(out var uid, out var motherboardComponent))
        {
            if (motherboardComponent.Machine is not { } machine)
                continue;

            if (!machine.IsPowered())
                continue;

            Log.Info($"Shutdown motherboard `{ToPrettyString(uid)}`");

            TryTurnOff(uid, motherboardComponent);

            machine.Shutdown();
            machine.Dispose();

            motherboardComponent.Machine = null;
        }
    }

    private void OnComponentStartup(EntityUid uid, CpuComponent component, ComponentStartup args)
    {
        if (!_enabled)
            return;

        if (component.Machine is not null)
            return;

        var proto = _prototype.Index<CpuPrototype>(component.Prototype);
        var totalMachines = Machine.MachinesCount();

        if (totalMachines > (ulong)_maxMachines)
        {
            Log.Warning($"Machines count `{totalMachines}` reached the cap!");
            return;
        }

        if (_memoryUsed + proto.Memory > _maxMemory)
        {
            Log.Warning("Machines memory usage reached the cap!");
            return;
        }

        Log.Info($"Creating machine on motherboard `{proto.Name}` `{ToPrettyString(uid)}`");

        component.Name = proto.Name;
        component.Config = MachineConfig.Default() with { MemSize = proto.Memory, IPQ = (ulong)proto.Ipq };
        component.DrawRate = proto.DrawRate;

        _flashMemory.InitFlashMemory(uid, null, proto.FlashMemorySize);

        _memoryUsed += component.Config.MemSize;
    }
}

[ByRefEvent]
[PublicAPI]
public record struct MachineTurnedOffEvent(EntityUid Machine);

[ByRefEvent]
[PublicAPI]
public record struct MachineStartAttempt(EntityUid Machine, bool Cancelled);

[ByRefEvent]
[PublicAPI]
public record struct MachineStartedEvent(EntityUid Machine);
