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

using Content.Server.ModularComputer.SerialNumber;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server.ModularComputer.Devices.Pci;

public abstract class PciDeviceSystem<TComponent, TState> : DeviceSystem<TComponent, TState>
    where TComponent : PciDeviceComponent<TState>
    where TState : DeviceState, new()
{
    [Dependency] private readonly PciBusDeviceSystem _pciBus = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<TComponent, DetachedFromPciBusEvent>(OnDetachedFromPciBus);
        SubscribeLocalEvent<TComponent, AttachedToPciBusEvent>(OnAttachedToPciBus);
        SubscribeLocalEvent<TComponent, ExaminedEvent>(OnExamined);
    }

    protected virtual void OnExamined(EntityUid uid, TComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !component.Device.ShowInExamine)
            return;

        args.PushMarkup(Loc.GetString("modular-computers-pci-device-examine-pci-port"));
    }

    [PublicAPI]
    protected virtual void OnComponentStartup(EntityUid uid, TComponent component, ComponentStartup args)
    {
        component.Device.Owner = uid;

        if (TryComp(uid, out SerialNumberComponent? serialNumberComponent))
            component.Device.Uuid = serialNumberComponent.Uuid;
    }

    [PublicAPI]
    protected virtual void OnAttachedToPciBus(EntityUid uid, TComponent component, ref AttachedToPciBusEvent args)
    {
        if (component.Motherboard is not null)
            return;

        AttachCallbacks(uid, component, component.Device.MmioDevice);

        if (_pciBus.TryAttachDevice(args.PciBus, args.Component, component.Device))
            component.Motherboard = args.PciBus;
    }

    [PublicAPI]
    protected virtual void OnDetachedFromPciBus(EntityUid uid, TComponent component, ref DetachedFromPciBusEvent args)
    {
        if (component.Motherboard is null)
            return;

        _pciBus.TryDetachDevice(args.PciBus, args.Component, component.Device);
        component.Motherboard = null;
        component.Device.MmioDevice.MmioRead = null;
        component.Device.MmioDevice.MmioWrite = null;
    }
}
