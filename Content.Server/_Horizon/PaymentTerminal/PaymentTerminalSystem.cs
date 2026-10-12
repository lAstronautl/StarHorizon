using System.Diagnostics.CodeAnalysis;
using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared._Horizon.PaymentTerminal;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._Horizon.PaymentTerminal;

public sealed class PaymentTerminalSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PaymentTerminalComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PaymentTerminalComponent, ExaminedEvent>(OnExamined);

        Subs.BuiEvents<PaymentTerminalComponent>(PaymentTerminalUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<PaymentTerminalKeypadMessage>(OnKeypad);
            subs.Event<PaymentTerminalClearMessage>(OnClear);
            subs.Event<PaymentTerminalSelectFieldMessage>(OnSelectField);
            subs.Event<PaymentTerminalToggleModeMessage>(OnToggleMode);
            subs.Event<PaymentTerminalUnbindMessage>(OnUnbind);
        });
    }

    private void OnExamined(Entity<PaymentTerminalComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.OwnerIdName is { } name)
            args.PushMarkup(Loc.GetString("payment-terminal-examine-bound", ("name", name)));
        else
            args.PushMarkup(Loc.GetString("payment-terminal-examine-unbound"));
    }

    private void OnUiOpened(Entity<PaymentTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnInteractUsing(Entity<PaymentTerminalComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_idCard.TryGetIdCard(args.Used, out var idCard))
            return;

        args.Handled = true;

        if (ent.Comp.OwnerUserId == null)
            TryBind(ent, args.User, idCard.Comp.FullName);
        else
            TryPay(ent, args.User);
    }

    private void TryBind(Entity<PaymentTerminalComponent> ent, EntityUid user, string? idName)
    {
        if (!HasComp<BankAccountComponent>(user)
            || !_player.TryGetSessionByEntity(user, out var session)
            || !_prefs.TryGetCachedPreferences(session.UserId, out var prefs)
            || prefs.SelectedCharacter is not HumanoidCharacterProfile profile)
        {
            Deny(ent, user, "payment-terminal-no-account");
            return;
        }

        ent.Comp.OwnerUserId = session.UserId;
        ent.Comp.OwnerCharacterName = profile.Name;
        ent.Comp.OwnerIdName = string.IsNullOrWhiteSpace(idName) ? profile.Name : idName;

        _audio.PlayPvs(ent.Comp.SuccessSound, ent);
        _popup.PopupEntity(Loc.GetString("payment-terminal-bound", ("name", ent.Comp.OwnerIdName)), ent, user);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low, $"{ToPrettyString(user):actor} bound payment terminal {ToPrettyString(ent):entity} to their account");
        UpdateUi(ent);
    }

    private void TryPay(Entity<PaymentTerminalComponent> ent, EntityUid user)
    {
        var comp = ent.Comp;

        if (comp.Price <= 0)
        {
            Deny(ent, user, "payment-terminal-no-price");
            return;
        }

        if (IsOwner(comp, user))
        {
            Deny(ent, user, "payment-terminal-self-pay");
            return;
        }

        if (!TryGetOwnerAccount(comp, out _, out _, out _))
        {
            Deny(ent, user, "payment-terminal-owner-unavailable");
            return;
        }

        var total = comp.Price + comp.Tip;
        if (!_bank.TryBankWithdraw(user, total))
        {
            Deny(ent, user, "payment-terminal-insufficient-funds", ("amount", total));
            return;
        }

        var tax = (int) Math.Floor(total * comp.TaxRate);
        var payout = total - tax;

        // Look the owner up again: the withdrawal may have replaced their cached preferences if they share a player account.
        if (payout > 0 && !TryDepositToOwner(comp, payout))
        {
            _bank.TryBankDeposit(user, total);
            Deny(ent, user, "payment-terminal-transaction-failed");
            return;
        }

        if (tax > 0)
            _bank.TrySectorDeposit(comp.TaxAccount, tax, LedgerEntryType.VendorTax);

        _audio.PlayPvs(comp.SuccessSound, ent);
        _popup.PopupEntity(Loc.GetString("payment-terminal-paid", ("amount", total), ("name", comp.OwnerIdName ?? string.Empty)), ent, user);

        if (comp.OwnerUserId is { } ownerId
            && _player.TryGetSessionById(ownerId, out var ownerSession)
            && ownerSession.AttachedEntity is { } ownerMob)
        {
            _popup.PopupEntity(Loc.GetString("payment-terminal-received", ("amount", payout), ("tax", tax)), ownerMob, ownerMob);
        }

        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(user):actor} paid {total} (price {comp.Price}, tip {comp.Tip}, tax {tax}) to {comp.OwnerCharacterName} via payment terminal {ToPrettyString(ent):entity}");

        comp.Tip = 0;
        if (comp.Mode == PaymentTerminalMode.Free)
            comp.Price = 0;

        UpdateUi(ent);
    }

    private bool TryDepositToOwner(PaymentTerminalComponent comp, int amount)
    {
        if (!TryGetOwnerAccount(comp, out var session, out var prefs, out var profile))
            return false;

        // Prefer the entity overload when the owner is playing that character, so their balance display stays in sync.
        if (session.AttachedEntity is { } mob
            && HasComp<BankAccountComponent>(mob)
            && ReferenceEquals(prefs.SelectedCharacter, profile))
        {
            return _bank.TryBankDeposit(mob, amount);
        }

        return _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }

    /// <summary>
    /// Bank accounts live in the character profiles, which are only cached while the owner is connected.
    /// </summary>
    private bool TryGetOwnerAccount(PaymentTerminalComponent comp,
        [NotNullWhen(true)] out ICommonSession? session,
        [NotNullWhen(true)] out PlayerPreferences? prefs,
        [NotNullWhen(true)] out HumanoidCharacterProfile? profile)
    {
        session = null;
        prefs = null;
        profile = null;

        if (comp.OwnerUserId is not { } ownerId
            || !_player.TryGetSessionById(ownerId, out session)
            || !_prefs.TryGetCachedPreferences(ownerId, out prefs))
        {
            return false;
        }

        foreach (var character in prefs.Characters.Values)
        {
            if (character is HumanoidCharacterProfile humanoid && humanoid.Name == comp.OwnerCharacterName)
            {
                profile = humanoid;
                return true;
            }
        }

        return false;
    }

    private bool IsOwner(PaymentTerminalComponent comp, EntityUid user)
    {
        return comp.OwnerUserId is { } ownerId
               && _player.TryGetSessionByEntity(user, out var session)
               && session.UserId == ownerId
               && _prefs.TryGetCachedPreferences(ownerId, out var prefs)
               && prefs.SelectedCharacter is HumanoidCharacterProfile profile
               && profile.Name == comp.OwnerCharacterName;
    }

    /// <summary>
    /// Owner-only settings are open to anyone until the terminal is bound.
    /// </summary>
    private bool CanConfigure(PaymentTerminalComponent comp, EntityUid user)
    {
        return comp.OwnerUserId == null || IsOwner(comp, user);
    }

    private bool CanEdit(PaymentTerminalComponent comp, PaymentTerminalField field, EntityUid user)
    {
        return field == PaymentTerminalField.Tip
               || comp.Mode == PaymentTerminalMode.Free
               || CanConfigure(comp, user);
    }

    private void OnKeypad(Entity<PaymentTerminalComponent> ent, ref PaymentTerminalKeypadMessage args)
    {
        if (args.Digit is < 0 or > 9)
            return;

        var comp = ent.Comp;
        if (!CanEdit(comp, comp.SelectedField, args.Actor))
        {
            Deny(ent, args.Actor, "payment-terminal-owner-only");
            return;
        }

        var current = comp.SelectedField == PaymentTerminalField.Price ? comp.Price : comp.Tip;
        var next = (long) current * 10 + args.Digit;
        if (next > comp.MaxAmount)
            return;

        if (comp.SelectedField == PaymentTerminalField.Price)
            comp.Price = (int) next;
        else
            comp.Tip = (int) next;

        _audio.PlayPvs(comp.KeypadSound, ent, AudioParams.Default.WithVolume(-5f));
        UpdateUi(ent);
    }

    private void OnClear(Entity<PaymentTerminalComponent> ent, ref PaymentTerminalClearMessage args)
    {
        var comp = ent.Comp;
        if (!CanEdit(comp, comp.SelectedField, args.Actor))
        {
            Deny(ent, args.Actor, "payment-terminal-owner-only");
            return;
        }

        if (comp.SelectedField == PaymentTerminalField.Price)
            comp.Price = 0;
        else
            comp.Tip = 0;

        UpdateUi(ent);
    }

    private void OnSelectField(Entity<PaymentTerminalComponent> ent, ref PaymentTerminalSelectFieldMessage args)
    {
        if (!Enum.IsDefined(args.Field))
            return;

        ent.Comp.SelectedField = args.Field;
        UpdateUi(ent);
    }

    private void OnToggleMode(Entity<PaymentTerminalComponent> ent, ref PaymentTerminalToggleModeMessage args)
    {
        var comp = ent.Comp;
        if (!CanConfigure(comp, args.Actor))
        {
            Deny(ent, args.Actor, "payment-terminal-owner-only");
            return;
        }

        comp.Mode = comp.Mode == PaymentTerminalMode.Fixed ? PaymentTerminalMode.Free : PaymentTerminalMode.Fixed;
        comp.Price = 0;
        comp.Tip = 0;
        UpdateUi(ent);
    }

    private void OnUnbind(Entity<PaymentTerminalComponent> ent, ref PaymentTerminalUnbindMessage args)
    {
        var comp = ent.Comp;
        if (comp.OwnerUserId == null)
            return;

        if (!IsOwner(comp, args.Actor))
        {
            Deny(ent, args.Actor, "payment-terminal-owner-only");
            return;
        }

        _adminLog.Add(LogType.ATMUsage, LogImpact.Low, $"{ToPrettyString(args.Actor):actor} unbound payment terminal {ToPrettyString(ent):entity}");

        comp.OwnerUserId = null;
        comp.OwnerCharacterName = null;
        comp.OwnerIdName = null;
        comp.Price = 0;
        comp.Tip = 0;

        _popup.PopupEntity(Loc.GetString("payment-terminal-unbound"), ent, args.Actor);
        UpdateUi(ent);
    }

    private void Deny(EntityUid terminal, EntityUid user, string message, params (string, object)[] args)
    {
        _audio.PlayPvs(Comp<PaymentTerminalComponent>(terminal).ErrorSound, terminal);
        _popup.PopupEntity(Loc.GetString(message, args), terminal, user);
    }

    private void UpdateUi(Entity<PaymentTerminalComponent> ent)
    {
        var comp = ent.Comp;
        var state = new PaymentTerminalBoundUserInterfaceState(
            comp.OwnerIdName,
            comp.Mode,
            comp.SelectedField,
            comp.Price,
            comp.Tip,
            comp.TaxRate);

        _ui.SetUiState(ent.Owner, PaymentTerminalUiKey.Key, state);
    }
}
