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

using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.ModularComputer.Case;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using JetBrains.Annotations;

namespace Content.Server.ModularComputer.Devices.Pci;

public sealed class PciSlotsSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PciSlotsComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PciSlotsComponent, ExaminedEvent>(OnExaminedEvent);
        SubscribeLocalEvent<PciSlotsComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<PciSlotsComponent, ComputerCaseToolDoAfterEvent>(OnComputerCaseToolDoAfter);
        SubscribeLocalEvent<PciSlotsComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<PciSlotsComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PciSlotsComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<PciSlotsComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PciSlotsComponent, AfterInteractEvent>(OnAfterInteractEvent);
    }

    private void OnGetVerbs(EntityUid uid, PciSlotsComponent component, GetVerbsEvent<Verb> args)
    {
        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            RaiseLocalEvent(pciDevice, args);
        }
    }

    private void OnInteractUsing(EntityUid uid, PciSlotsComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var handled = false;

        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            var ev = new InteractUsingEvent(args.User, args.Used, args.Target, args.ClickLocation);
            RaiseLocalEvent(pciDevice, ev);

            handled |= ev.Handled;
        }

        args.Handled = handled;
    }

    private void OnAfterInteractEvent(EntityUid uid, PciSlotsComponent component, AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        var handled = false;

        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            var ev = new AfterInteractEvent(args.User, args.Used, args.Target, args.ClickLocation, args.CanReach);
            RaiseLocalEvent(pciDevice, ev);

            handled |= ev.Handled;
        }

        args.Handled = handled;
    }

    private void OnActivateInWorld(EntityUid uid, PciSlotsComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<ItemComponent>(uid))
            return;

        var handled = false;

        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            var ev = new ActivateInWorldEvent(args.User, pciDevice, false);
            RaiseLocalEvent(pciDevice, ev);

            handled |= ev.Handled;
        }

        args.Handled = handled;
    }

    private void OnUseInHand(EntityUid uid, PciSlotsComponent component, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<ItemComponent>(uid))
            return;

        var handled = false;

        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            var ev = new UseInHandEvent(args.User);
            RaiseLocalEvent(pciDevice, ev);

            handled |= ev.Handled;
        }

        args.Handled = handled;
    }

    [PublicAPI]
    public IEnumerable<EntityUid> EnumeratePciDevices(EntityUid uid, PciSlotsComponent? component)
    {
        if (!Resolve(uid, ref component, false))
            yield break;

        var xForm = Transform(uid);
        var children = xForm.ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!_tag.HasTag(child, "Pci"))
                continue;

            yield return child;
        }
    }

    private void OnComponentShutdown(EntityUid uid, PciSlotsComponent component, ComponentShutdown args)
    {
        foreach (var pciSlot in component.PciSlots)
        {
            _itemSlots.RemoveItemSlot(uid, pciSlot);
        }

        component.PciSlots.Clear();
        component.Overrides.Clear();
    }

    private void OnComputerCaseToolDoAfter(EntityUid uid, PciSlotsComponent component,
        ComputerCaseToolDoAfterEvent args)
    {
        component.IsLocked = !component.IsLocked;

        foreach (var pciSlot in component.PciSlots)
        {
            _itemSlots.SetLock(uid, pciSlot, component.IsLocked);
        }
    }

    private void OnComponentInit(EntityUid uid, PciSlotsComponent component, ComponentInit args)
    {
        for (var i = 0; i < component.Count; i++)
        {
            component.Overrides.TryGetValue(i, out var slot);

            slot ??= new ItemSlot { Whitelist = component.DefaultWhitelist, Blacklist = component.DefaultBlacklist };
            slot.Name = Loc.GetString($"modular-computers-pci-slot-{i}");
            slot.Swap = false;

            _itemSlots.AddItemSlot(uid, $"pci_{i}", slot);
            _itemSlots.SetLock(uid, slot, component.IsLocked);

            component.PciSlots.Add(slot);
        }
    }

    private void OnExaminedEvent(EntityUid uid, PciSlotsComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var metadataQuery = GetEntityQuery<MetaDataComponent>();
        var devices = string.Empty;

        args.PushMarkup(Loc.GetString("modular-computers-cpu-pci-slots", ("count", component.Count)));

        foreach (var pciDevice in EnumeratePciDevices(uid, component))
        {
            if (!HasComp<ShowPciDeviceInCaseComponent>(pciDevice))
                continue;

            if (devices.Length != 0)
                devices += ", ";

            devices += $"[color=white]{metadataQuery.GetComponent(pciDevice).EntityName}[/color]";
        }

        if (string.IsNullOrEmpty(devices))
            return;

        args.PushMarkup(Loc.GetString("modular-computers-pci-devices-in-case", ("devices", devices)));
    }
}
