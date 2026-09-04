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
using Content.Server.Interaction;
using Content.Server.ModularComputer.Cpu;
using Content.Server.NTVM;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Content.Shared.ModularComputer.Debugger;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.ModularComputer.Debugger;

public sealed class DebuggerSystem : EntitySystem
{
    [Dependency] private readonly CpuSystem _cpu = default!;

    [Dependency] private readonly InteractionSystem _interaction = default!;

    [Dependency] private readonly PopupSystem _popup = default!;

    [Dependency] private readonly IGameTiming _timing = default!;

    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private TimeSpan _nextUpdate = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DebuggerComponent, BeforeRangedInteractEvent>(BeforeRangedInteract);
        SubscribeLocalEvent<DebuggerComponent, DebuggerTogglePowerMessage>(OnDebuggerTogglePower);
    }

    private void OnDebuggerTogglePower(EntityUid uid, DebuggerComponent component, DebuggerTogglePowerMessage args)
    {
        if (component.Target is not { } target)
            return;

        _cpu.TogglePower(target, null);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime <= _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<DebuggerComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Target is not { } target)
            {
                UpdateUiState(uid, component);
                continue;
            }

            if (!_interaction.InRangeUnobstructed(uid, target))
                component.Target = null;

            UpdateUiState(uid, component);
        }
    }

    private void BeforeRangedInteract(EntityUid uid, DebuggerComponent component, BeforeRangedInteractEvent args)
    {
        if (args.Handled)
            return;

        if (args.Target is null)
            return;

        if (!TryComp(args.Target, out CpuComponent? _))
            return;

        if (component.Target != args.Target)
            _popup.PopupEntity(Loc.GetString("modular-computers-debugger-connected"), args.Target.Value);

        component.Target = args.Target;
        args.Handled = true;
    }

    private MotherboardState? GetMotherboardState(DebuggerComponent component)
    {
        if (component.Target is not { } motherUid)
            return null;

        if (!TryComp(component.Target, out CpuComponent? motherComp))
            return null;

        if (!_cpu.IsPowered(motherUid, motherComp))
            return new MotherboardState(false, new List<HartState>(), new List<MmioDeviceState>());

        var harts = GetHartStates(motherUid, motherComp);
        var mmioDevices = GetMmioDeviceStates(motherUid, motherComp);

        return new MotherboardState(true, harts, mmioDevices);
    }

    private List<MmioDeviceState> GetMmioDeviceStates(EntityUid uid, CpuComponent component)
    {
        var states = new List<MmioDeviceState>();
        var id = 0;

        foreach (var device in _cpu.GetMmioDevices(uid, component).OrderBy(d => d.Address))
        {
            states.Add(new MmioDeviceState(id, device.Label, device.Address, device.Size));
            id += 1;
        }

        return states;
    }

    private List<HartState> GetHartStates(EntityUid uid, CpuComponent component)
    {
        var harts = _cpu.GetHarts(uid, component);
        var states = new List<HartState>();

        foreach (var hart in harts)
        {
            var registers = new Dictionary<ulong, ulong>();

            foreach (var register in Enum.GetValues<Register>())
            {
                registers.Add((ulong)register, hart.ReadRegister(register));
            }

            states.Add(new HartState(registers));
        }

        return states;
    }

    private void UpdateUiState(EntityUid uid, DebuggerComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_ui.HasUi(uid, DebuggerUiKey.Key))
            return;

        var motherboardState = GetMotherboardState(component);
        var state = new DebuggerBoundUserInterfaceState(motherboardState);

        _ui.SetUiState(uid, DebuggerUiKey.Key, state);
    }
}
