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
using Content.Server.ModularComputer.Devices.Pci;
using Content.Server.NTVM;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Prometheus;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.HardDrive;

public sealed class HardDriveDeviceSystem : PciDeviceSystem<HardDriveDeviceComponent, HardDriveDeviceState>
{
    private static readonly Gauge HardDrivesDiskLogicMemoryUsage =
        Metrics.CreateGauge("machines_hdd_disk_logic_memory_usage",
            "Disk logic memory used by machines HDD (in bytes)");

    private static readonly Gauge HardDrivesDiskPhysMemoryUsage =
        Metrics.CreateGauge("machines_hdd_disk_phys_memory_usage", "Disk phys memory used by machines HDD (in bytes)");

    [Dependency] private readonly AudioSystem _audio = default!;

    [Dependency] private readonly IGameTiming _gameTiming = default!;

    private TimeSpan _nextMetricsUpdate = TimeSpan.Zero;

    private VirtualDisksManager _virtualDisks = default!;

    public override void Initialize()
    {
        base.Initialize();

        _virtualDisks = new VirtualDisksManager("hdd", "hdd", Log);

        SubscribeLocalEvent<HardDriveDeviceComponent, ComponentShutdown>(OnComponentShutdown);
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
        var query = EntityQueryEnumerator<HardDriveDeviceComponent>();

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

        HardDrivesDiskLogicMemoryUsage.Set(logicMemoryUsed);
        HardDrivesDiskPhysMemoryUsage.Set(physMemoryUsed);
    }

    protected override void OnDeviceEvent(EntityUid uid, HardDriveDeviceComponent component, DeviceEvent ev)
    {
        if (ev is HddAccessEvent)
        {
            if (_gameTiming.CurTime < component.NextSound)
                return;

            component.NextSound = _gameTiming.CurTime + TimeSpan.FromMilliseconds(500);

            if (component.AccessSounds is { } accessSounds)
                _audio.PlayPvs(accessSounds, uid);
        }
    }

    [PublicAPI]
    public bool TryDumpHdd(EntityUid uid, HardDriveDeviceComponent? component, [NotNullWhen(true)] out ResPath? path)
    {
        path = null;

        if (!Resolve(uid, ref component))
            return false;

        path = UpdateState<ResPath?>(uid, component, state =>
        {
            if (state.Disk is null)
                return null;

            return _virtualDisks.TryDumpDisk(state.Disk, "hdd");
        });

        return path is not null;
    }

    private void OnComponentShutdown(EntityUid uid, HardDriveDeviceComponent component, ComponentShutdown args)
    {
        UpdateState(uid, component, state =>
        {
            if (state.Disk is null)
                return;

            _virtualDisks.DeleteDisk(state.Disk);
            state.Disk = null;
        });
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _virtualDisks.DeleteAll();
        _virtualDisks.CreateRoot();
    }

    protected override void OnComponentStartup(EntityUid uid, HardDriveDeviceComponent component, ComponentStartup args)
    {
        base.OnComponentStartup(uid, component, args);

        UpdateState(uid, component, state =>
        {
            if (component.Preload is not null)
                state.Disk = _virtualDisks.CreateDiskFromCopy(component.Preload.Value, null);
            else
                state.Disk = _virtualDisks.CreateDisk(component.Size);

            DebugTools.AssertNotNull(state.Disk);
        });
    }

    private static void OpBulkRead(Machine machine, HardDriveDeviceState state)
    {
        if (state.Disk is not { } disk)
        {
            state.OpResult = (double)HardDriveError.Unknown;
            return;
        }

        var args = state.Arguments;
        var address = (long)args[0];
        var size = (int)args[1];
        var dstAddress = (ulong)args[2];

        if (size is <= 0 or > HardDriveDeviceComponent.MaxReadWriteSize)
        {
            state.OpResult = (double)HardDriveError.InvalidSize;
            return;
        }

        if ((int)address >= disk.Size || (int)address + size > disk.Size)
        {
            state.OpResult = (double)HardDriveError.InvalidAddress;
            return;
        }

        var data = new byte[size];
        disk.Read(data, address);

        machine.WriteRam(data, dstAddress);

        state.TryEnqueueEvent(new HddAccessEvent());
        state.OpResult = (double)HardDriveError.Ok;
    }

    private static void OpBulkWrite(Machine machine, HardDriveDeviceState state)
    {
        if (state.Disk is not { } disk)
        {
            state.OpResult = (double)HardDriveError.Unknown;
            return;
        }

        var args = state.Arguments;
        var address = (long)args[0];
        var size = (int)args[1];
        var srcAddress = (ulong)args[2];

        if (size is <= 0 or > HardDriveDeviceComponent.MaxReadWriteSize)
        {
            state.OpResult = (double)HardDriveError.InvalidSize;
            return;
        }

        if ((int)address >= disk.Size || (int)address + size > disk.Size)
        {
            state.OpResult = (double)HardDriveError.InvalidAddress;
            return;
        }


        var data = machine.ReadRam(srcAddress, size);
        disk.Write(data, address);

        state.TryEnqueueEvent(new HddAccessEvent());
        state.OpResult = (double)HardDriveError.Ok;
    }

    private static void TryCatchOpCall(Machine machine, HardDriveDeviceState state, HardDriveOp op)
    {
        try
        {
            switch (op)
            {
                case HardDriveOp.BulkRead:
                    OpBulkRead(machine, state);

                    break;
                case HardDriveOp.BulkWrite:
                    OpBulkWrite(machine, state);

                    break;
            }
        }
        catch (Exception)
        {
            state.OpResult = (int)HardDriveError.Unknown;
        }
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, HardDriveDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= HardDriveDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - HardDriveDeviceComponent.ArgumentsOffset, 0,
                HardDriveDeviceComponent.Arguments - 1);

            state.Arguments[argIndex] = data.ReadDouble();
            return true;
        }

        var register = (DeviceWriteRegister)offset;

        switch (register)
        {
            case DeviceWriteRegister.CallOp:
            {
                TryCatchOpCall(machine, state, (HardDriveOp)data.ReadUInt());

                break;
            }
        }

        return true;
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, HardDriveDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= HardDriveDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - HardDriveDeviceComponent.ArgumentsOffset, 0,
                HardDriveDeviceComponent.Arguments - 1);

            state.Arguments[argIndex] = data.ReadDouble();
            return true;
        }

        var register = (DeviceReadRegister)offset;

        switch (register)
        {
            case DeviceReadRegister.OpResult:
            {
                data.Write(state.OpResult);

                break;
            }
            case DeviceReadRegister.Size:
            {
                data.Write(state.Disk?.Size ?? 0);

                break;
            }
        }

        return true;
    }

    private sealed class HddAccessEvent : DeviceEvent
    {
    }

    private enum DeviceReadRegister : byte
    {
        OpResult = 0x0,
        Size = 0x1
    }

    private enum DeviceWriteRegister : byte
    {
        CallOp = 0x0
    }

    private enum HardDriveOp
    {
        BulkRead = 0x0,
        BulkWrite = 0x1
    }
}
