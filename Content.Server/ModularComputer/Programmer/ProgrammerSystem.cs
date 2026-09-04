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

using System.IO;
using Content.Server.DoAfter;
using Content.Server.ModularComputer.Bootrom;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.ModularComputer.Programmer;
using JetBrains.Annotations;
using Robust.Server.GameObjects;

namespace Content.Server.ModularComputer.Programmer;

public sealed class ProgrammerSystem : SharedProgrammerSystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    [Dependency] private readonly BootromSystem _bootrom = default!;

    [Dependency] private readonly DoAfterSystem _doAfter = default!;

    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProgrammerComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<ProgrammerComponent, BoundUIOpenedEvent>(OnBoundUIOpened);
        SubscribeLocalEvent<ProgrammerComponent, AfterInteractEvent>(OnInteractUsingEvent);
        SubscribeLocalEvent<ProgrammerComponent, ProgrammerDoAfterEvent>(OnProgrammerDoAfter);
        SubscribeNetworkEvent<UploadBootromEvent>(OnUploadBootrom);
    }

    private void OnComponentInit(EntityUid uid, ProgrammerComponent component, ComponentInit args)
    {
        EnsureComp<BootromComponent>(uid);
    }

    [PublicAPI]
    public void LoadBootrom(EntityUid uid, ProgrammerComponent? component, EntityUid from,
        BootromComponent fromComponent)
    {
        if (!Resolve(uid, ref component))
            return;

        _bootrom.CopyFrom(uid, Comp<BootromComponent>(uid), from, fromComponent);

        component.State = ProgrammerState.Ready;
        Dirty(uid, component);
        UpdateUiState(uid, component);
    }

    private void OnProgrammerDoAfter(EntityUid uid, ProgrammerComponent component, ProgrammerDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (component.State != ProgrammerState.Ready)
            return;

        if (args.Target is null)
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User)} programmed {ToPrettyString(args.Target.Value)} with {ToPrettyString(uid)}");
        _bootrom.CopyFrom(args.Target.Value, null, uid, null);

        args.Handled = true;
    }

    private void OnInteractUsingEvent(EntityUid uid, ProgrammerComponent component, AfterInteractEvent args)
    {
        if (!TryComp(args.Target, out BootromComponent? _))
            return;

        if (component.State != ProgrammerState.Ready)
            return;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
#if DEBUG
            1f,
#else
            5f,
#endif
            new ProgrammerDoAfterEvent(), uid, args.Target, uid)
        {
            BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true, NeedHand = true
        });

        args.Handled = true;
    }

    private void OnBoundUIOpened(EntityUid uid, ProgrammerComponent component, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, component);
    }

    private void OnUploadBootrom(UploadBootromEvent ev)
    {
        if (ev.Data.Length > MaxBootromSize)
            return;

        var uid = GetEntity(ev.EntityUid);

        if (!TryComp<ProgrammerComponent>(uid, out var component))
            return;

        if (!TryComp<BootromComponent>(uid, out var bootromComponent))
            return;

        if (component.State == ProgrammerState.Loading)
            return;

        var oldState = component.State;
        component.State = ProgrammerState.Loading;
        UpdateUiState(uid, component);

        if (!_bootrom.TryLoadFromStream(uid, bootromComponent, new MemoryStream(ev.Data), out _))
            component.State = oldState;
        else
            component.State = ProgrammerState.Ready;

        Dirty(uid, component);
        UpdateUiState(uid, component);
    }

    private void UpdateUiState(EntityUid uid, ProgrammerComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_ui.HasUi(uid, ProgrammerUiKey.Key))
            return;

        var state = new ProgrammerBoundUserInterfaceState(GetNetEntity(uid), component.State, MaxBootromSize);

        _ui.SetUiState(uid, ProgrammerUiKey.Key, state);
    }
}
