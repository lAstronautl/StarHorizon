using Content.Server._Horizon.StationDeployment.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Events;
using Content.Shared.Cargo.Components;
using Content.Shared.Coordinates;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Horizon.StationDeployment;

/// <summary>
/// Handles deposit/withdraw for the purchasable StationUpgradeAtmComponent - functionally the same
/// as the station control console's bank ATM, but only while the appliance is installed
/// (StationUpgradeEquipmentComponent.Installed) and still parented to the grid it was bought for.
/// </summary>
public sealed class StationUpgradeAtmSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationUpgradeAtmComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StationUpgradeAtmComponent, StationBankWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<StationUpgradeAtmComponent, StationBankDepositMessage>(OnDeposit);
        SubscribeLocalEvent<StationUpgradeAtmComponent, EntInsertedIntoContainerMessage>(OnCashSlotChanged);
        SubscribeLocalEvent<StationUpgradeAtmComponent, EntRemovedFromContainerMessage>(OnCashSlotChanged);
    }

    private void OnUiOpened(Entity<StationUpgradeAtmComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnWithdraw(Entity<StationUpgradeAtmComponent> ent, ref StationBankWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!IsOperational(ent, out var station) || !TryComp<StationBankAccountComponent>(station, out var bank))
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

    private void OnDeposit(Entity<StationUpgradeAtmComponent> ent, ref StationBankDepositMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!IsOperational(ent, out var station) || !TryComp<StationBankAccountComponent>(station, out var bank))
            return;

        GetInsertedCashAmount(ent.Comp, out var deposit);

        if (ent.Comp.CashSlot.ContainerSlot is not BaseContainer cashSlot || deposit <= 0)
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

    private void OnCashSlotChanged(Entity<StationUpgradeAtmComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        UpdateUi(ent);
    }

    private void OnCashSlotChanged(Entity<StationUpgradeAtmComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateUi(ent);
    }

    /// <summary>
    /// The ATM only works once installed and while it's still on the grid it was bought for - moving
    /// it off that grid (or before activation) disables it.
    /// </summary>
    private bool IsOperational(Entity<StationUpgradeAtmComponent> ent, out EntityUid station)
    {
        station = default;

        if (!TryComp<StationUpgradeEquipmentComponent>(ent.Owner, out var equipment) ||
            !equipment.Installed ||
            Transform(ent.Owner).GridUid != equipment.BoundGrid)
        {
            return false;
        }

        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } owningStation)
            return false;

        station = owningStation;
        return true;
    }

    private void UpdateUi(Entity<StationUpgradeAtmComponent> ent)
    {
        GetInsertedCashAmount(ent.Comp, out var deposit);

        if (!IsOperational(ent, out var station) || !TryComp<StationBankAccountComponent>(station, out var bank))
        {
            _uiSystem.SetUiState(ent.Owner, BankATMMenuUiKey.ATM, new StationBankATMMenuInterfaceState(0, false, deposit));
            return;
        }

        _uiSystem.SetUiState(ent.Owner, BankATMMenuUiKey.ATM, new StationBankATMMenuInterfaceState(GetBalance(bank), true, deposit));
    }

    private static int GetBalance(StationBankAccountComponent bank)
    {
        return bank.Accounts.GetValueOrDefault(bank.PrimaryAccount, 0);
    }

    private void GetInsertedCashAmount(StationUpgradeAtmComponent component, out int amount)
    {
        amount = 0;
        var cashEntity = component.CashSlot.ContainerSlot?.ContainedEntity;

        if (cashEntity == null)
            return;

        if (!TryComp<StackComponent>(cashEntity, out var cashStack) || cashStack.StackTypeId != component.CashType)
        {
            amount = -1;
            return;
        }

        amount = cashStack.Count;
    }

    private void SetInsertedCashAmount(StationUpgradeAtmComponent component, int amount, out int leftAmount, out bool empty)
    {
        leftAmount = 0;
        empty = false;
        var cashEntity = component.CashSlot.ContainerSlot?.ContainedEntity;

        if (!TryComp<StackComponent>(cashEntity, out var cashStack) || cashStack.StackTypeId != component.CashType)
            return;

        cashStack.Count -= amount;
        leftAmount = cashStack.Count;

        if (cashStack.Count <= 0)
            empty = true;
    }
}
