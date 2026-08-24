using Content.Server._Horizon.StationDeployment.Components;
using Content.Server._NF.Tools.Components;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared._NF.BindToStation;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.StationRecords;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Horizon.StationDeployment;

/// <summary>
/// Handles activating purchased station upgrade equipment by swiping the station owner's ID card
/// on it - anchors it and turns it on. Server-side because it needs to verify station ownership,
/// which the client shouldn't be trusted to check.
/// </summary>
public sealed class StationUpgradeEquipmentSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationUpgradeEquipmentComponent, AfterInteractUsingEvent>(OnIdCardSwipe);
        SubscribeLocalEvent<StationUpgradeEquipmentComponent, MapInitEvent>(OnMapInit);
    }

    // Some of the underlying ATM prototypes disable tool use (anti-theft on their vanilla usage) -
    // station upgrade equipment should be removable with a screwdriver/crowbar like anything else.
    private void OnMapInit(Entity<StationUpgradeEquipmentComponent> ent, ref MapInitEvent args)
    {
        RemComp<DisableToolUseComponent>(ent.Owner);
    }

    private void OnIdCardSwipe(Entity<StationUpgradeEquipmentComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || ent.Comp.Installed)
            return;

        var idCardUid = args.Used;
        if (!TryComp<StationRecordKeyStorageComponent>(idCardUid, out _))
            return;

        args.Handled = true;

        if (!TryComp<StationBoundObjectComponent>(ent, out var bound) ||
            bound.BoundStation is not { Valid: true } boundStation ||
            _station.GetOwningStation(ent.Owner) != boundStation)
        {
            _popup.PopupEntity(Loc.GetString("station-upgrade-equipment-wrong-grid"), ent, args.User, PopupType.MediumCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            return;
        }

        if (!TryComp<StationDeedComponent>(idCardUid, out var deed) || deed.StationUid != boundStation)
        {
            _popup.PopupEntity(Loc.GetString("station-upgrade-equipment-not-owner"), ent, args.User, PopupType.MediumCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            return;
        }

        ent.Comp.Installed = true;
        _transform.AnchorEntity(ent.Owner, Transform(ent.Owner));

        _popup.PopupEntity(Loc.GetString("station-upgrade-equipment-installed"), ent, args.User, PopupType.Medium);
        _audio.PlayPvs(ent.Comp.ActivateSound, ent);
    }
}
