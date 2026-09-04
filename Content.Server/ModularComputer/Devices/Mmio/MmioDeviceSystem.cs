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
using JetBrains.Annotations;

namespace Content.Server.ModularComputer.Devices.Mmio;

public abstract class MmioDeviceSystem<TComponent, TState> : DeviceSystem<TComponent, TState>
    where TComponent : MmioDeviceComponent<TState>
    where TState : DeviceState, new()
{
    [Dependency] private readonly CpuSystem _cpu = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TComponent, ComponentShutdown>(OnComponentShutdown);
    }

    [PublicAPI]
    protected virtual void OnComponentStartup(EntityUid uid, TComponent component, ComponentStartup args)
    {
        if (component.Motherboard is not null)
            return;

        if (!TryComp(uid, out CpuComponent? motherboardComponent))
            return;

        AttachCallbacks(uid, component, component.Device);

        if (_cpu.TryAttachMmioDevice(uid, motherboardComponent, component.Device))
            component.Motherboard = uid;
    }

    [PublicAPI]
    protected virtual void OnComponentShutdown(EntityUid uid, TComponent component, ComponentShutdown args)
    {
        if (component.Motherboard is not { } motherboard)
            return;

        if (!TryComp(motherboard, out CpuComponent? motherboardComponent))
            return;

        _cpu.TryDetachMmioDevice(motherboard, motherboardComponent, component.Device);
        component.Motherboard = null;
        component.Device.MmioRead = null;
        component.Device.MmioWrite = null;
    }
}
