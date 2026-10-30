using Content.Server._Horizon.Shipbreaking.Derelict;
using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.Mobs.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._Horizon.Shipbreaking;

public sealed class ScrapProcessorSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly DerelictYardSystem _derelict = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScrapProcessorComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<ScrapProcessorComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!this.IsPowered(ent, EntityManager))
            return;

        if (Transform(other).Anchored || HasComp<MobStateComponent>(other) || HasComp<MapGridComponent>(other))
            return;

        if (_whitelist.IsWhitelistPass(ent.Comp.Blacklist, other))
            return;

        if (!_whitelist.IsWhitelistPassOrNull(ent.Comp.Whitelist, other))
        {
            if (!ent.Comp.DestroyRejected)
                return;

            QueueDel(other);
            return;
        }

        var payout = (int) (_pricing.GetPrice(other) * ent.Comp.PayoutMultiplier);
        if (payout > 0 && _bank.TrySectorDeposit(ent.Comp.Account, payout, LedgerEntryType.StationDepositAssetsSold))
            _derelict.CreditScrap(other, payout);

        _audio.PlayPvs(ent.Comp.Sound, ent);
        QueueDel(other);
    }
}
