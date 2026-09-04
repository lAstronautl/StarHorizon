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
using Content.Server.ModularComputer.Devices.Keyboard;
using Content.Server.ModularComputer.Devices.Mouse;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.ModularComputer.Case;
using Content.Shared.Power;
using Content.Shared.PowerCell;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.ModularComputer.Case;

public sealed class ComputerCaseSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    [Dependency] private readonly AudioSystem _audio = default!;

    [Dependency] private readonly CpuSystem _cpu = default!;

    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    [Dependency] private readonly PowerCellSystem _powerCell = default!;

    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComputerCaseComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ComputerCaseComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbsEvent);
        SubscribeLocalEvent<ComputerCaseComponent, MachineStartAttempt>(OnMachineStartAttempt);
        SubscribeLocalEvent<ComputerCaseComponent, MachineStartedEvent>(OnMachineStarted);
        SubscribeLocalEvent<ComputerCaseComponent, MachineTurnedOffEvent>(OnMachineTurnedOff);
        SubscribeLocalEvent<ComputerCaseComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ComputerCaseComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ComputerCaseComponent, ComputerCaseToolDoAfterEvent>(OnComputerCaseToolDoAfter);
        SubscribeLocalEvent<ComputerCaseComponent, PowerCellSlotEmptyEvent>(OnPowerSlotEmpty);
        SubscribeLocalEvent<ComputerCaseComponent, ItemSlotEjectAttemptEvent>(OnBeforeRemovedFromItemSlots);
        SubscribeLocalEvent<ComputerCaseComponent, MouseDeviceClickedEvent>(OnMouseDeviceClicked);
        SubscribeLocalEvent<ComputerCaseComponent, KeyboardDeviceKeyPressedEvent>(OnKeyboardDeviceKeyPressed);
        SubscribeLocalEvent<ComputerCaseComponent, EntRemovedFromContainerMessage>(OnEntRemovedFromContainer);
    }

    private void OnEntRemovedFromContainer(EntityUid uid, ComputerCaseComponent component,
        EntRemovedFromContainerMessage args)
    {
        if (component.MotherboardSlot.Item is not { } motherboard)
            return;

        RaiseLocalEvent(motherboard, args);
    }

    private void OnKeyboardDeviceKeyPressed(EntityUid uid, ComputerCaseComponent component,
        ref KeyboardDeviceKeyPressedEvent args)
    {
        if (component.KeyboardPressSound is null)
            return;

        _audio.PlayPvs(component.KeyboardPressSound, args.Keyboard,
            AudioParams.Default.WithVariation(0.05f));
    }

    private void OnMouseDeviceClicked(EntityUid uid, ComputerCaseComponent component, ref MouseDeviceClickedEvent args)
    {
        if (component.MouseClickSound is null)
            return;

        _audio.PlayPvs(component.MouseClickSound, args.Mouse,
            AudioParams.Default.WithVariation(0.05f));
    }

    private void OnBeforeRemovedFromItemSlots(EntityUid uid, ComputerCaseComponent component,
        ref ItemSlotEjectAttemptEvent args)
    {
        if (TryComp<CpuComponent>(args.Item, out var cpuComponent))
        {
            _cpu.TryTurnOff(args.Item, cpuComponent);
            TurnOff(uid, component);
        }
    }

    private void OnPowerSlotEmpty(EntityUid uid, ComputerCaseComponent component, ref PowerCellSlotEmptyEvent args)
    {
        if (component.MotherboardSlot.Item is not { } cpu)
            return;

        if (!this.IsPowered(uid, EntityManager))
            _cpu.TryTurnOff(cpu, null);
    }

    private void OnPowerChanged(EntityUid uid, ComputerCaseComponent component, ref PowerChangedEvent args)
    {
        if (component.MotherboardSlot.Item is not { } cpu)
            return;

        if (component.PowerCellSlot is not null)
            return;

        if (!args.Powered)
            _cpu.TryTurnOff(cpu, null);
    }

    private void OnMachineTurnedOff(EntityUid uid, ComputerCaseComponent component, ref MachineTurnedOffEvent args)
    {
        TurnOff(uid, component);
    }

    private void TurnOff(EntityUid uid, ComputerCaseComponent component)
    {
        _appearance.SetData(uid, ComputerCaseVisuals.On, false);

        if (component.PowerCellSlot is not null)
            EnsureComp<PowerCellDrawComponent>(uid).DrawRate = 0;
        else
            EnsureComp<ApcPowerReceiverComponent>(uid).Load = 0;
    }

    private void OnMachineStarted(EntityUid uid, ComputerCaseComponent component, ref MachineStartedEvent args)
    {
        _appearance.SetData(uid, ComputerCaseVisuals.On, true);

        if (component.PowerCellSlot is not null)
        {
            var drawComponent = EnsureComp<PowerCellDrawComponent>(uid);
            drawComponent.UseRate = 1;
            drawComponent.Enabled = true;
            Dirty(uid, drawComponent);
        }
        else
        {
            var cpuComponent = Comp<CpuComponent>(component.MotherboardSlot.Item!.Value);
            var apcPowerReceiverComponent = EnsureComp<ApcPowerReceiverComponent>(uid);

            apcPowerReceiverComponent.Load = cpuComponent.DrawRate;
        }
    }

    private void OnComputerCaseToolDoAfter(EntityUid uid, ComputerCaseComponent component,
        ComputerCaseToolDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        var newState = !component.MotherboardSlot.Locked;

        if (component.PowerCellSlot is { } powerCellSlot)
            _itemSlots.SetLock(uid, powerCellSlot, newState);

        _itemSlots.SetLock(uid, component.MotherboardSlot, newState);

        args.Handled = true;
    }

    private void OnInteractUsing(EntityUid uid, ComputerCaseComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ToolComponent>(args.Used, out var tool))
            return;

        if (_tool.UseTool(args.Used, args.User, uid, ComputerCaseComponent.ScrewTime, "Screwing",
                new ComputerCaseToolDoAfterEvent(), toolComponent: tool))
            args.Handled = true;
    }

    private void OnMachineStartAttempt(EntityUid uid, ComputerCaseComponent component, ref MachineStartAttempt ev)
    {
        if (ev.Cancelled)
            return;

        DebugTools.AssertNotNull(component.MotherboardSlot.Item);

        if (component.PowerCellSlot is null)
            return;

        var drawComponent = EnsureComp<PowerCellDrawComponent>(uid);

        drawComponent.UseRate = 1;

        if (!_powerCell.HasActivatableCharge(uid))
            ev.Cancelled = true;
    }

    private void OnComponentInit(EntityUid uid, ComputerCaseComponent component, ComponentInit args)
    {
        if (component.PowerCellSlotId is not null)
        {
            _itemSlots.TryGetSlot(uid, component.PowerCellSlotId, out var powerCellSlot);
            component.PowerCellSlot = powerCellSlot;

            DebugTools.AssertNotNull(component.PowerCellSlot);
        }

        _itemSlots.TryGetSlot(uid, component.MotherboardSlotId, out var motherboardSlot);
        component.MotherboardSlot = motherboardSlot!;

        DebugTools.AssertNotNull(component.MotherboardSlot);
    }

    private void OnGetAlternativeVerbsEvent(EntityUid uid, ComputerCaseComponent component,
        GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("modular-computers-cpu-verb-toggle-power"),
            Act = () =>
            {
                if (component.MotherboardSlot.Item is not { Valid: true } motherboard)
                    return;

                _cpu.TogglePower(motherboard, null);
            }
        });
    }
}
