using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.Mobs.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Server._Horizon.Shipbreaking;

/// <summary>
/// Handles <see cref="ScrapProcessorComponent"/>: buys unanchored entities that touch the machine
/// for their price and pays the station bank account.
/// </summary>
public sealed class ScrapProcessorSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
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

        // Never eat things that are still part of a grid (walls next to the machine) or living things.
        if (Transform(other).Anchored || HasComp<MobStateComponent>(other))
            return;

        if (!_whitelist.CheckBoth(other, ent.Comp.Blacklist, ent.Comp.Whitelist))
            return;

        var payout = (int) (_pricing.GetPrice(other) * ent.Comp.PayoutMultiplier);
        if (payout > 0)
            _bank.TrySectorDeposit(ent.Comp.Account, payout, LedgerEntryType.StationDepositAssetsSold);

        _audio.PlayPvs(ent.Comp.Sound, ent);
        QueueDel(other);
    }
}
