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

using Content.Server.ModularComputer.Cpu;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.ModularComputer.Devices.Plic;
using Content.Server.NTVM;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Devices.Pci;

public sealed class PciBusDeviceSystem : MmioDeviceSystem<PciBusDeviceComponent, PciBusDeviceState>
{
    [Dependency] private readonly CpuSystem _cpu = default!;

    [Dependency] private readonly PciSlotsSystem _pciSlots = default!;

    [Dependency] private readonly PlicDeviceSystem _plicDevice = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PciBusDeviceComponent, MachineTurnedOffEvent>(OnMachineTurnedOff);
        SubscribeLocalEvent<PciBusDeviceComponent, MachineStartedEvent>(OnMachineStarted,
            after: new[] { typeof(PlicDeviceSystem) });
        SubscribeLocalEvent<PciBusDeviceComponent, EntRemovedFromContainerMessage>(OnEntRemovedFromContainer);
    }

    private void OnMachineTurnedOff(EntityUid uid, PciBusDeviceComponent component, ref MachineTurnedOffEvent args)
    {
        var machine = args.Machine;

        ForEachChildPciDevice(uid, device =>
        {
            var ev = new MachineTurnedOffEvent(machine);
            RaiseLocalEvent(device, ref ev);
        });

        Reset(uid, component);
    }

    private void ForEachChildPciDevice(EntityUid uid, Action<EntityUid> func)
    {
        var xForm = Transform(uid);

        foreach (var pciDevice in _pciSlots.EnumeratePciDevices(xForm.ParentUid, null))
        {
            func.Invoke(pciDevice);
        }
    }

    private void OnMachineStarted(EntityUid uid, PciBusDeviceComponent component, ref MachineStartedEvent args)
    {
        DebugTools.AssertNotNull(component.Motherboard);

        var motherboard = component.Motherboard!.Value;

        UpdateState(uid, component, state =>
        {
            var plicComponent = Comp<PlicDeviceComponent>(motherboard);

            for (var i = 0; i < PciBusDeviceComponent.MaxDevices; i++)
            {
                state.Irq[i] = _plicDevice.AllocIrq(motherboard, plicComponent);
            }
        });

        var ev = new AttachedToPciBusEvent(uid, component, motherboard);
        ForEachChildPciDevice(uid, child => RaiseLocalEvent(child, ref ev));
    }

    private void OnEntRemovedFromContainer(EntityUid uid, PciBusDeviceComponent component,
        EntRemovedFromContainerMessage args)
    {
        if (component.Motherboard is null)
            return;

        var containerId = args.Container.ID;

        if (!containerId.StartsWith("pci_"))
            return;

        var ev = new DetachedFromPciBusEvent(uid, component, component.Motherboard.Value);
        RaiseLocalEvent(args.Entity, ref ev);
    }

    protected override void OnMmioDeviceDetached(EntityUid uid, PciBusDeviceComponent component,
        ref MmioDeviceDetachedEvent args)
    {
        if (component.Motherboard is null)
            return;

        var ev = new DetachedFromPciBusEvent(uid, component, component.Motherboard.Value);
        ForEachChildPciDevice(uid, child => RaiseLocalEvent(child, ref ev));

        base.OnMmioDeviceDetached(uid, component, ref args);
    }

    protected override void OnComponentShutdown(EntityUid uid, PciBusDeviceComponent component, ComponentShutdown args)
    {
        Reset(uid, component);

        base.OnComponentShutdown(uid, component, args);
    }

    private void Reset(EntityUid uid, PciBusDeviceComponent component)
    {
        if (component.Motherboard is null)
            return;

        if (!TryComp(component.Motherboard, out CpuComponent? _))
            return;

        var ev = new DetachedFromPciBusEvent(uid, component, component.Motherboard.Value);
        ForEachChildPciDevice(uid, child => RaiseLocalEvent(child, ref ev));

        UpdateState(uid, component, state =>
        {
            state.Devices.Clear();
            state.MemoryAddress = PciBusDeviceComponent.Address + PciBusDeviceComponent.Size;
        });
    }

    [PublicAPI]
    public bool TryAttachDevice(EntityUid uid, PciBusDeviceComponent? component, PciDevice device)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (component.Motherboard is null)
            return false;

        var motherboard = component.Motherboard.Value;
        var ret = UpdateState(uid, component, state =>
        {
            if (state.Devices.Count >= PciBusDeviceComponent.MaxDevices)
                return false;

            var mmioDevice = device.MmioDevice;

            device.IrqPin = state.Irq[state.Devices.Count + 1];

            mmioDevice.Address = state.MemoryAddress + (mmioDevice.Size - state.MemoryAddress) % mmioDevice.Size;
            state.MemoryAddress = mmioDevice.Address + mmioDevice.Size;

            if (!_cpu.TryAttachMmioDevice(motherboard, null, mmioDevice))
                Log.Error($"Can't attach a PCI device {mmioDevice.Label} to {ToPrettyString(motherboard)}");
            else
                Log.Debug($"Attached a PCI device {mmioDevice.Label} to {ToPrettyString(motherboard)}");

            state.Devices.Add(device);

            return true;
        });

        return ret;
    }

    [PublicAPI]
    public bool TryDetachDevice(EntityUid uid, PciBusDeviceComponent? component, PciDevice device)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (component.Motherboard is null)
            return false;

        var motherboard = component.Motherboard.Value;

        if (!TryComp(motherboard, out CpuComponent? motherboardComponent))
            return false;

        if (!_cpu.TryDetachMmioDevice(component.Motherboard.Value, motherboardComponent, device.MmioDevice))
            Log.Warning($"Can't detach a PCI device {device.MmioDevice.Label} from {ToPrettyString(motherboard)}");
        else
            Log.Debug($"PCI device {device.MmioDevice.Label} detached from {ToPrettyString(motherboard)}");

        var ret = UpdateState(uid, component, state => state.Devices.Remove(device));

        return ret;
    }

    [PublicAPI]
    public List<PciDevice> GetDevices(EntityUid uid, PciBusDeviceComponent? component)
    {
        if (!Resolve(uid, ref component))
            return new List<PciDevice>();

        return UpdateState(uid, component, state => state.Devices);
    }

    protected override bool OnMmioRead(EntityUid uid, Machine machine, MmioDevice device, PciBusDeviceState state,
        BinaryRw data, int offset)
    {
        //
        // 16           8          0
        // +------------+----------+
        // | Device Idx | Register |
        // +------------+----------+
        //       8            8
        //
        var deviceIdx = (offset >> 8) & 0xFF;
        offset &= 0xFF;

        if (deviceIdx >= PciBusDeviceComponent.MaxDevices)
        {
            data.Write(0xFFFFFFFF);

            return true;
        }

        if (!state.Devices.TryGetValue(deviceIdx, out var pciDevice))
        {
            data.Write(0xFFFFFFFF);

            return true;
        }

        if (offset is >= PciBusDeviceComponent.UuidRegisterOffset
            and < PciBusDeviceComponent.UuidRegisterOffset + PciBusDeviceComponent.UuidLength)
        {
            var uuid = pciDevice.Uuid.ToByteArray();
            var idx = offset - PciBusDeviceComponent.UuidRegisterOffset;

            data.Write(uuid[idx]);
            return true;
        }

        var reg = (DeviceReadRegister)offset;

        switch (reg)
        {
            case DeviceReadRegister.Info:
                //
                // Send PCI device info
                //
                // 64  40        32          16          0
                // +---+---------+-----------+-----------+
                // | - | IRQ Pin | Device ID | Vendor ID |
                // +---+---------+-----------+-----------+
                //   24     8          16          16
                //

                var vendorBits = (long)pciDevice.VendorId;
                var idBits = (long)pciDevice.DeviceId << 16;
                var pinBits = (long)pciDevice.IrqPin << 32;

                var info = pinBits | idBits | vendorBits;

                data.Write(info);

                break;
            case DeviceReadRegister.Address:
                data.Write(pciDevice.MmioDevice.Address);

                return true;
        }

        return true;
    }

    private enum DeviceReadRegister : byte
    {
        Info = 0x0,
        Address = 0x1
    }
}

[PublicAPI]
[ByRefEvent]
public record struct AttachedToPciBusEvent(EntityUid PciBus, PciBusDeviceComponent Component, EntityUid Motherboard);

[PublicAPI]
[ByRefEvent]
public record struct DetachedFromPciBusEvent(EntityUid PciBus, PciBusDeviceComponent Component, EntityUid Motherboard);
