using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.StationRecords;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Horizon.StationDeployment;

/// <summary>
/// Handles swiping an ID card on a <see cref="StationDeploymentKitComponent"/> to link/unlink it,
/// ahead of the actual deploy step (server-side, see StationDeploymentSystem).
/// </summary>
public sealed class StationDeploymentKitSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationRecordKeyStorageComponent, AfterInteractEvent>(OnIdCardSwipe);
    }

    private void OnIdCardSwipe(EntityUid idCardUid, StationRecordKeyStorageComponent _, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (args.Target is not { Valid: true } target || !args.CanReach)
            return;

        // Is this id card interacting with a deployment kit? If not, ignore it.
        if (!TryComp<StationDeploymentKitComponent>(target, out var kit))
            return;

        args.Handled = true;

        if (TryComp<StationDeedComponent>(idCardUid, out var deed) && deed.StationUid != null)
        {
            _popup.PopupClient(Loc.GetString("station-deployment-kit-id-already-deeded"), target, args.User, PopupType.Medium);
            _audio.PlayLocal(kit.ErrorSound, target, args.User);
            return;
        }

        if (kit.LinkedIdCard == idCardUid)
        {
            _popup.PopupClient(Loc.GetString("station-deployment-kit-unlinked"), target, args.User, PopupType.Medium);
            _audio.PlayLocal(kit.SwipeSound, target, args.User);
            kit.LinkedIdCard = null;
        }
        else
        {
            _popup.PopupClient(Loc.GetString("station-deployment-kit-linked"), target, args.User, PopupType.Medium);
            _audio.PlayLocal(kit.SwipeSound, target, args.User);
            kit.LinkedIdCard = idCardUid;
        }

        Dirty(target, kit);
    }
}
