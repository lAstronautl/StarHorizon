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

using System.Linq;
using Content.Server.ModularComputer.Devices.Mmio;
using Content.Server.NTVM;
using JetBrains.Annotations;
using Robust.Shared.Collections;

namespace Content.Server.ModularComputer.Cpu;

public sealed partial class CpuSystem
{
    private static bool OnMmioAccess(EntityUid uid, CpuComponent component, uint address, byte[] data,
        MemoryAccess access)
    {
        if (component.Machine is null)
            return false;

        MmioDevice? device = null;
        var devices = new ValueList<MmioDevice>(component.MmioDevices);

        foreach (var mmioDevice in devices)
        {
            if (address < mmioDevice.Address)
                continue;

            if (address + data.Length > (long)(mmioDevice.Address + mmioDevice.Size))
                continue;

            device = mmioDevice;
            break;
        }

        if (device is null)
            return false;

        var offset = (int)(address - device.Address);
        var mmioData = new BinaryRw(data);

        switch (access)
        {
            case MemoryAccess.Read:
                return device.MmioRead?.Invoke(component.Machine, device, mmioData, offset) ?? true;
            case MemoryAccess.Write:
                return device.MmioWrite?.Invoke(component.Machine, device, mmioData, offset) ?? true;
            default:
                return false;
        }
    }

    [PublicAPI]
    public bool TryAttachMmioDevice(EntityUid uid, CpuComponent? component, MmioDevice newDevice)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (GetMmioDevices(uid, component).Contains(newDevice))
        {
            Log.Warning($"Trying to attach {newDevice.Label} to {ToPrettyString(uid)} twice");

            return false;
        }

        var memBase = component.Machine?.GetRAMBase() ?? component.Config.MemBase;

        if (newDevice.Address >= memBase)
        {
            Log.Error($"MMIO device's {newDevice.Label} address is located in RAM");
            return false;
        }

        foreach (var device in component.MmioDevices)
        {
            if (newDevice.Address >= device.Address &&
                newDevice.Address + newDevice.Size <= device.Address + device.Size)
            {
                Log.Error($"Can't attach a {newDevice.Label} on {ToPrettyString(uid)}");
                return false;
            }
        }

        component.MmioDevices.Add(newDevice);

        var xForm = Transform(uid);
        var afterEv = new MmioDeviceAttachedEvent(newDevice);

        RaiseLocalEvent(ref afterEv);
        RaiseLocalEvent(xForm.ParentUid, ref afterEv);

        Log.Debug($"MMIO device {newDevice.Label} attached to {ToPrettyString(uid)}");
        return true;
    }

    [PublicAPI]
    public bool TryDetachMmioDevice(EntityUid uid, CpuComponent? component, MmioDevice device)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!GetMmioDevices(uid, component).Contains(device))
            return false;

        component.MmioDevices.Remove(device);

        var xForm = Transform(uid);
        var afterEv = new MmioDeviceDetachedEvent(device);

        RaiseLocalEvent(ref afterEv);
        RaiseLocalEvent(xForm.ParentUid, ref afterEv);

        Log.Debug($"MMIO device {device.Label} detached from {ToPrettyString(uid)}");
        return true;
    }

    [PublicAPI]
    public IEnumerable<MmioDevice> GetMmioDevices(EntityUid uid, CpuComponent? component)
    {
        if (!Resolve(uid, ref component))
            return Enumerable.Empty<MmioDevice>();

        return component.MmioDevices;
    }
}

[PublicAPI]
[ByRefEvent]
public record struct MmioDeviceAttachedEvent(MmioDevice Device);

[PublicAPI]
[ByRefEvent]
public record struct MmioDeviceDetachedEvent(MmioDevice Device);
