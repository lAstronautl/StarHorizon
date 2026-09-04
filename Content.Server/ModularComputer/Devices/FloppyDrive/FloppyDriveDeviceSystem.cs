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
using Content.Server.ModularComputer.FloppyDisk;
using Content.Server.NTVM;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Server.ModularComputer.Devices.FloppyDrive;

public sealed class FloppyDriveDeviceSystem : PciDeviceSystem<FloppyDriveDeviceComponent, FloppyDriveDeviceState>
{
    [Dependency] private readonly AudioSystem _audio = default!;

    [Dependency] private readonly ContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloppyDriveDeviceComponent, InteractUsingEvent>(OnInteractUsingEvent);
        SubscribeLocalEvent<FloppyDriveDeviceComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    protected override void OnDeviceEvent(EntityUid uid, FloppyDriveDeviceComponent component, DeviceEvent ev)
    {
        if (ev is FloppyDiskAccessEvent)
        {
            if (component.AccessSounds is null)
                return;

            _audio.PlayPvs(component.AccessSounds, uid);
        }
        else if (ev is EjectFloppyDiskEvent)
            TryEjectDisk(uid, component, out _);
    }

    protected override void OnComponentStartup(EntityUid uid, FloppyDriveDeviceComponent component,
        ComponentStartup args)
    {
        base.OnComponentStartup(uid, component, args);

        component.FloppyContainer = _container.EnsureContainer<Container>(uid, component.FloppyContainerId);
    }

    private void OnGetVerbs(EntityUid uid, FloppyDriveDeviceComponent component, GetVerbsEvent<Verb> args)
    {
        if (component.FloppyContainer.ContainedEntities.Count == 0)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("modular-computers-floppy-drive-verb-eject-disk"),
            Act = () => TryEjectDisk(uid, component, out _)
        });
    }

    private void OnInteractUsingEvent(EntityUid uid, FloppyDriveDeviceComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp(args.Used, out FloppyDiskComponent? diskComponent))
            return;

        if (component.FloppyContainer.ContainedEntities.Count != 0)
            return;

        if (!_container.Insert(args.Used, component.FloppyContainer, force: true))
            return;

        if (component.InsertSound is { } insertSound)
            _audio.PlayPvs(insertSound, uid);

        UpdateState(uid, component, state => { state.Disk = diskComponent.Disk; });

        args.Handled = true;
    }

    [PublicAPI]
    public bool TryEjectDisk(EntityUid uid, FloppyDriveDeviceComponent? component,
        [NotNullWhen(true)] out EntityUid? disk)
    {
        disk = null;

        if (!Resolve(uid, ref component))
            return false;

        if (component.FloppyContainer.ContainedEntities.Count == 0)
            return false;

        disk = component.FloppyContainer.ContainedEntities[0];

        if (!_container.Remove(disk.Value, component.FloppyContainer, force: true))
        {
            disk = null;
            return false;
        }

        UpdateState(uid, component, state => { state.Disk = null; });

        if (component.EjectSound is { } ejectSound)
            _audio.PlayPvs(ejectSound, uid);

        return true;
    }

    private static void OpBulkRead(Machine machine, FloppyDriveDeviceState state)
    {
        if (state.Disk is not { } disk)
        {
            state.OpResult = (double)FloppyDriveError.FloppyDriveIsEmpty;
            return;
        }

        var args = state.Arguments;
        var address = (long)args[0];
        var size = (int)args[1];
        var dstAddress = (ulong)args[2];

        if (size is <= 0 or > FloppyDriveDeviceComponent.MaxReadWriteSize)
        {
            state.OpResult = (double)FloppyDriveError.InvalidSize;
            return;
        }

        if ((int)address >= disk.Size || (int)address + size > disk.Size)
        {
            state.OpResult = (double)FloppyDriveError.InvalidAddress;
            return;
        }

        var data = new byte[size];
        disk.Read(data, address);

        machine.WriteRam(data, dstAddress);

        state.TryEnqueueEvent(new FloppyDiskAccessEvent());
        state.OpResult = (double)FloppyDriveError.Ok;
    }

    private static void OpBulkWrite(Machine machine, FloppyDriveDeviceState state)
    {
        if (state.Disk is not { } disk)
        {
            state.OpResult = (double)FloppyDriveError.FloppyDriveIsEmpty;
            return;
        }

        var args = state.Arguments;
        var address = (long)args[0];
        var size = (int)args[1];
        var srcAddress = (ulong)args[2];

        if (size is <= 0 or > FloppyDriveDeviceComponent.MaxReadWriteSize)
        {
            state.OpResult = (double)FloppyDriveError.InvalidSize;
            return;
        }

        if ((int)address >= disk.Size || (int)address + size > disk.Size)
        {
            state.OpResult = (double)FloppyDriveError.InvalidAddress;
            return;
        }


        var data = machine.ReadRam(srcAddress, size);
        disk.Write(data, address);

        state.TryEnqueueEvent(new FloppyDiskAccessEvent());
        state.OpResult = (double)FloppyDriveError.Ok;
    }

    private static void TryCatchOpCall(Machine machine, FloppyDriveDeviceState state, HardDriveOp op)
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
            state.OpResult = (int)FloppyDriveError.Unknown;
        }
    }

    protected override bool OnMmioWrite(EntityUid uid, Machine machine, MmioDevice device, FloppyDriveDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= FloppyDriveDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - FloppyDriveDeviceComponent.ArgumentsOffset, 0,
                FloppyDriveDeviceComponent.Arguments - 1);

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
            case DeviceWriteRegister.EjectDisk:
            {
                state.TryEnqueueEvent(new EjectFloppyDiskEvent());

                break;
            }
        }

        return true;
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, FloppyDriveDeviceState state,
        BinaryRw data, int offset)
    {
        if (offset >= FloppyDriveDeviceComponent.ArgumentsOffset)
        {
            var argIndex = Math.Clamp(offset - FloppyDriveDeviceComponent.ArgumentsOffset, 0,
                FloppyDriveDeviceComponent.Arguments - 1);

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

    private sealed class FloppyDiskAccessEvent : DeviceEvent
    {
    }

    private sealed class EjectFloppyDiskEvent : DeviceEvent
    {
    }

    private enum DeviceReadRegister : byte
    {
        OpResult = 0x0,
        Size = 0x1
    }

    private enum DeviceWriteRegister : byte
    {
        CallOp = 0x0,
        EjectDisk = 0x1
    }

    private enum HardDriveOp
    {
        BulkRead = 0x0,
        BulkWrite = 0x1
    }
}
