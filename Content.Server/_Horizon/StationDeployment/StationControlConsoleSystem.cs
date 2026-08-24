using Content.Server._Horizon.StationDeployment.Components;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.StationDeployment;
using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared._Horizon.StationDeployment.Prototypes;
using Content.Shared._NF.Bank.Events;
using Content.Shared.Cargo.Components;
using Content.Shared.CCVar;
using Content.Shared.Coordinates;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.StationDeployment;

/// <summary>
/// Lets a player rename the station a <see cref="StationControlConsoleComponent"/> belongs to, and
/// deposit/withdraw from the station's own bank account (the one credited by capsule sales) -
/// combines the rename console and a StationAdminBankATM-style ATM into a single console.
/// The station starts out with a random name/code (assigned by StationNameSetup on deployment).
/// </summary>
public sealed class StationControlConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;

    private int _maxNameLength;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationControlConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StationControlConsoleComponent, StationControlConsoleRenameMessage>(OnRename);
        SubscribeLocalEvent<StationControlConsoleComponent, StationControlConsoleSetIffColorMessage>(OnSetIffColor);
        SubscribeLocalEvent<StationControlConsoleComponent, StationBankWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<StationControlConsoleComponent, StationBankDepositMessage>(OnDeposit);
        SubscribeLocalEvent<StationControlConsoleComponent, StationControlConsolePurchaseUpgradeMessage>(OnPurchaseUpgrade);
        SubscribeLocalEvent<StationControlConsoleComponent, EntInsertedIntoContainerMessage>(OnCashSlotChanged);
        SubscribeLocalEvent<StationControlConsoleComponent, EntRemovedFromContainerMessage>(OnCashSlotChanged);

        Subs.CVar(_cfg, CCVars.MaxNameLength, value => _maxNameLength = value, true);
    }

    private void OnUiOpened(Entity<StationControlConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnRename(Entity<StationControlConsoleComponent> ent, ref StationControlConsoleRenameMessage args)
    {
        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station)
            return;

        var baseName = args.Name.Trim();
        if (baseName.Length == 0 || baseName.Length > _maxNameLength)
        {
            _popup.PopupEntity(Loc.GetString("station-control-console-rename-invalid"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        // The station's number/code is assigned once on deployment and never changes - only the
        // base name the player picks is editable here. The deed lives on the station's grid(s),
        // not on the abstract station entity.
        var number = FindStationDeed(station)?.StationNumber;
        var fullName = number is null ? baseName : $"{baseName} {number}";

        _station.RenameStation(station, fullName, loud: false);

        if (TryComp<StationDataComponent>(station, out var stationData))
        {
            foreach (var grid in stationData.Grids)
            {
                _metaData.SetEntityName(grid, fullName);

                if (TryComp<StationDeedComponent>(grid, out var gridDeed))
                    gridDeed.StationName = baseName;
            }
        }

        _popup.PopupEntity(Loc.GetString("station-control-console-rename-success"), ent, args.Actor, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnSetIffColor(Entity<StationControlConsoleComponent> ent, ref StationControlConsoleSetIffColorMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station ||
            !TryComp<StationDataComponent>(station, out var stationData))
        {
            return;
        }

        if (Color.TryFromHex(args.ColorHex) is not { } color)
        {
            _popup.PopupEntity(Loc.GetString("station-control-console-iff-color-invalid"), ent, player, PopupType.SmallCaution);
            return;
        }

        foreach (var grid in stationData.Grids)
        {
            _shuttle.SetIFFColor(grid, color);
        }

        _popup.PopupEntity(Loc.GetString("station-control-console-iff-color-success"), ent, player, PopupType.Medium);

        UpdateUi(ent);
    }

    private void OnWithdraw(Entity<StationControlConsoleComponent> ent, ref StationBankWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station ||
            !TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        var balance = GetBalance(bank);

        if (args.Amount <= 0 || args.Amount > balance)
        {
            _popup.PopupEntity(Loc.GetString("bank-insufficient-funds"), ent, player, PopupType.SmallCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            UpdateUi(ent);
            return;
        }

        _cargo.UpdateBankAccount((station, bank), -args.Amount, bank.PrimaryAccount);

        var stackPrototype = _protoMan.Index(ent.Comp.CashType);
        var stackUid = _stack.Spawn(args.Amount, stackPrototype, player.ToCoordinates());
        if (!_hands.TryPickupAnyHand(player, stackUid))
            _transform.SetLocalRotation(stackUid, Angle.Zero);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        UpdateUi(ent);
    }

    private void OnDeposit(Entity<StationControlConsoleComponent> ent, ref StationBankDepositMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station ||
            !TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        GetInsertedCashAmount(ent.Comp, out var deposit);

        if (ent.Comp.CashSlot.ContainerSlot is not BaseContainer cashSlot ||
            deposit <= 0)
        {
            _popup.PopupEntity(Loc.GetString("bank-atm-menu-wrong-cash"), ent, player, PopupType.SmallCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            UpdateUi(ent);
            return;
        }

        var amount = Math.Min(args.Amount, deposit);
        if (amount <= 0)
        {
            _popup.PopupEntity(Loc.GetString("bank-atm-menu-transaction-denied"), ent, player, PopupType.SmallCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            UpdateUi(ent);
            return;
        }

        _cargo.UpdateBankAccount((station, bank), amount, bank.PrimaryAccount);

        SetInsertedCashAmount(ent.Comp, amount, out _, out var empty);
        if (empty)
            _container.CleanContainer(cashSlot);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        UpdateUi(ent);
    }

    private void OnPurchaseUpgrade(Entity<StationControlConsoleComponent> ent, ref StationControlConsolePurchaseUpgradeMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!_protoMan.TryIndex(args.PurchaseId, out var purchase))
            return;

        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station ||
            !TryComp<StationDataComponent>(station, out var stationData) ||
            !TryComp<StationBankAccountComponent>(station, out var bank) ||
            !TryComp<StationDevelopmentComponent>(station, out var devel))
        {
            return;
        }

        var currentLevel = devel.Progress.GetValueOrDefault(purchase.Category, 0);
        if (currentLevel < purchase.RequiredLevel)
        {
            _popup.PopupEntity(Loc.GetString("station-control-console-upgrade-locked"), ent, player, PopupType.SmallCaution);
            return;
        }

        if (purchase.Price > GetBalance(bank))
        {
            _popup.PopupEntity(Loc.GetString("bank-insufficient-funds"), ent, player, PopupType.SmallCaution);
            return;
        }

        if (FindPurchasePallet(stationData) is not { } pallet)
        {
            _popup.PopupEntity(Loc.GetString("station-control-console-upgrade-no-pallet"), ent, player, PopupType.MediumCaution);
            return;
        }

        _cargo.UpdateBankAccount((station, bank), -purchase.Price, bank.PrimaryAccount);

        var spawned = EntityManager.SpawnEntity(purchase.Entity, Transform(pallet.Pallet).Coordinates);
        var equipment = EnsureComp<StationUpgradeEquipmentComponent>(spawned);
        equipment.BoundGrid = pallet.Grid;

        _popup.PopupEntity(Loc.GetString("station-control-console-upgrade-purchased", ("name", Loc.GetString(purchase.Name))), ent, player, PopupType.Medium);

        UpdateUi(ent);
    }

    /// <summary>
    /// Finds an anchored CargoPalletBuy-type pallet on one of the station's own grids to deliver a
    /// purchase to.
    /// </summary>
    private (EntityUid Pallet, EntityUid Grid)? FindPurchasePallet(StationDataComponent stationData)
    {
        var query = AllEntityQuery<CargoPalletComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pallet, out var xform))
        {
            if (!xform.Anchored || xform.GridUid is not { Valid: true } grid)
                continue;

            var onStation = false;
            foreach (var stationGrid in stationData.Grids)
            {
                if (stationGrid != grid)
                    continue;

                onStation = true;
                break;
            }

            if (!onStation || (pallet.PalletType & BuySellType.Buy) == 0)
                continue;

            return (uid, grid);
        }

        return null;
    }

    private void OnCashSlotChanged(Entity<StationControlConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        UpdateUi(ent);
    }

    private void OnCashSlotChanged(Entity<StationControlConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<StationControlConsoleComponent> ent)
    {
        var station = _station.GetOwningStation(ent.Owner);
        var name = station is { Valid: true } stationUid
            ? FindStationDeed(stationUid)?.StationName ?? MetaData(stationUid).EntityName
            : string.Empty;

        var balance = 0;
        var bankEnabled = false;
        if (station is { Valid: true } stationForBank && TryComp<StationBankAccountComponent>(stationForBank, out var bank))
        {
            balance = GetBalance(bank);
            bankEnabled = true;
        }

        GetInsertedCashAmount(ent.Comp, out var deposit);

        var iffColorHex = Color.White.ToHexNoAlpha();
        if (station is { Valid: true } stationForIff &&
            TryComp<StationDataComponent>(stationForIff, out var iffStationData))
        {
            foreach (var grid in iffStationData.Grids)
            {
                iffColorHex = _shuttle.GetIFFColor(grid).ToHexNoAlpha();
                break;
            }
        }

        var upgrades = new List<StationUpgradePurchaseUiEntry>();
        if (station is { Valid: true } stationForUpgrades &&
            TryComp<StationDevelopmentComponent>(stationForUpgrades, out var devel))
        {
            foreach (var purchase in _protoMan.EnumeratePrototypes<StationUpgradePurchasePrototype>())
            {
                var currentLevel = devel.Progress.GetValueOrDefault(purchase.Category, 0);
                upgrades.Add(new StationUpgradePurchaseUiEntry(
                    purchase.ID,
                    purchase.RequiredLevel,
                    currentLevel,
                    purchase.Price,
                    currentLevel >= purchase.RequiredLevel,
                    purchase.Price <= balance));
            }
        }

        _uiSystem.SetUiState(ent.Owner, StationControlConsoleUiKey.Key,
            new StationControlConsoleBuiState(name, balance, bankEnabled, deposit, iffColorHex, upgrades));
    }

    private static int GetBalance(StationBankAccountComponent bank)
    {
        return bank.Accounts.GetValueOrDefault(bank.PrimaryAccount, 0);
    }

    private void GetInsertedCashAmount(StationControlConsoleComponent component, out int amount)
    {
        amount = 0;
        var cashEntity = component.CashSlot.ContainerSlot?.ContainedEntity;

        if (cashEntity == null)
            return;

        // Invalid item inserted: amount should be negative (to denote an error).
        if (!TryComp<StackComponent>(cashEntity, out var cashStack) ||
            cashStack.StackTypeId != component.CashType)
        {
            amount = -1;
            return;
        }

        amount = cashStack.Count;
    }

    private void SetInsertedCashAmount(StationControlConsoleComponent component, int amount, out int leftAmount, out bool empty)
    {
        leftAmount = 0;
        empty = false;
        var cashEntity = component.CashSlot.ContainerSlot?.ContainedEntity;

        if (!TryComp<StackComponent>(cashEntity, out var cashStack) ||
            cashStack.StackTypeId != component.CashType)
        {
            return;
        }

        cashStack.Count -= amount;
        leftAmount = cashStack.Count;

        if (cashStack.Count <= 0)
            empty = true;
    }

    /// <summary>
    /// Finds the <see cref="StationDeedComponent"/> tracking this station's number/code - it lives on
    /// one of the station's grids rather than on the abstract station entity itself.
    /// </summary>
    private StationDeedComponent? FindStationDeed(EntityUid station)
    {
        if (!TryComp<StationDataComponent>(station, out var stationData))
            return null;

        foreach (var grid in stationData.Grids)
        {
            if (TryComp<StationDeedComponent>(grid, out var deed))
                return deed;
        }

        return null;
    }
}
