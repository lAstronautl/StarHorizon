using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._Horizon.Marketplace;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Stacks;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server._Horizon.Marketplace;

/// <summary>
/// Requests: a player posts what they want, how many and the reward, which is held from their account.
/// Others offer items for it; the offered item waits in storage until the requester accepts or rejects it.
/// Accepting pays the offerer the reward minus the sector tax and hands the item to the requester.
/// </summary>
public sealed partial class MarketplaceSystem
{
    private bool IsCharacter(EntityUid user, NetUserId userId, string characterName)
    {
        return TryGetSeller(user, out var session, out var profile)
               && session.UserId == userId
               && profile.Name == characterName;
    }

    #region Requests

    private void OnCreateRequest(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceCreateRequestMessage args)
    {
        var user = args.Actor;
        var comp = ent.Comp;

        if (!TryGetSeller(user, out var session, out var profile))
        {
            Deny(ent, user, "marketplace-error-no-account");
            return;
        }

        var title = args.Title.Trim();
        var description = args.Description.Trim();
        if (title.Length == 0 || title.Length > MarketplaceConsoleComponent.MaxTitleLength)
        {
            Deny(ent, user, "marketplace-request-error-title", ("max", MarketplaceConsoleComponent.MaxTitleLength));
            return;
        }

        if (description.Length > MarketplaceConsoleComponent.MaxDescriptionLength)
            description = description[..MarketplaceConsoleComponent.MaxDescriptionLength];

        if (args.Quantity <= 0 || args.Quantity > MarketplaceConsoleComponent.MaxRequestQuantity)
        {
            Deny(ent, user, "marketplace-request-error-quantity", ("max", MarketplaceConsoleComponent.MaxRequestQuantity));
            return;
        }

        if (args.Reward <= 0 || args.Reward > comp.MaxUnitPrice)
        {
            Deny(ent, user, "marketplace-error-bad-price", ("max", comp.MaxUnitPrice));
            return;
        }

        var storage = GetStorage();
        var openRequests = storage.Comp.Requests.Values.Count(r =>
            r.RequesterUserId == session.UserId && r.RequesterCharacterName == profile.Name);
        if (openRequests >= comp.MaxRequestsPerRequester)
        {
            Deny(ent, user, "marketplace-request-error-too-many", ("max", comp.MaxRequestsPerRequester));
            return;
        }

        // The reward is held until the request is fulfilled or cancelled.
        if (!_bank.TryBankWithdraw(user, args.Reward))
        {
            Deny(ent, user, "marketplace-error-insufficient-funds", ("amount", args.Reward));
            return;
        }

        var request = new MarketplaceRequest
        {
            Id = storage.Comp.NextRequestId++,
            Title = title,
            Description = description,
            Quantity = args.Quantity,
            Reward = args.Reward,
            RequesterUserId = session.UserId,
            RequesterCharacterName = profile.Name,
            RequesterIdName = GetIdName(user, profile),
            GridName = GetGridName(ent),
            CreatedAt = _timing.CurTime,
        };
        storage.Comp.Requests[request.Id] = request;

        _audio.PlayPvs(comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("marketplace-request-popup-created", ("title", title), ("reward", args.Reward)), ent, user);
        _adminLog.Add(LogType.ATMUsage,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} created marketplace request #{request.Id} \"{title}\" x{args.Quantity} for {args.Reward} via {ToPrettyString(ent):entity}");

        UpdateAllUis();
    }

    private void OnCancelRequest(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceCancelRequestMessage args)
    {
        var user = args.Actor;
        var storage = GetStorage();

        if (!storage.Comp.Requests.TryGetValue(args.RequestId, out var request))
        {
            Deny(ent, user, "marketplace-request-error-missing");
            return;
        }

        if (!IsCharacter(user, request.RequesterUserId, request.RequesterCharacterName))
        {
            Deny(ent, user, "marketplace-request-error-not-owner");
            return;
        }

        if (!_bank.TryBankDeposit(user, request.Reward))
        {
            Deny(ent, user, "marketplace-error-failed");
            return;
        }

        CloseRequest(storage, request);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("marketplace-request-popup-cancelled", ("title", request.Title), ("reward", request.Reward)), ent, user);
        _adminLog.Add(LogType.ATMUsage,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} cancelled marketplace request #{request.Id} and got {request.Reward} back");

        UpdateAllUis();
    }

    /// <summary>
    /// Removes a request and returns all its pending offers to their owners.
    /// </summary>
    private void CloseRequest(Entity<MarketplaceStorageComponent> storage, MarketplaceRequest request)
    {
        storage.Comp.Requests.Remove(request.Id);

        foreach (var offer in storage.Comp.Offers.Values)
        {
            if (offer.RequestId == request.Id && offer.Status == MarketplaceOfferStatus.Pending)
                offer.Status = MarketplaceOfferStatus.Returned;
        }
    }

    /// <summary>
    /// Requests can't survive the round, so give the held rewards back while players are still connected.
    /// </summary>
    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New == GameRunLevel.PostRound)
            RefundAll();
    }

    /// <summary>
    /// Returns held request rewards and pays out pending earnings before the storage is cleared.
    /// </summary>
    private void RefundAll()
    {
        if (_storage is not { } storageUid || !TryComp<MarketplaceStorageComponent>(storageUid, out var storage))
            return;

        foreach (var request in storage.Requests.Values.ToList())
        {
            if (TryDepositToSeller(request.RequesterUserId, request.RequesterCharacterName, request.Reward))
                continue;

            Log.Warning($"Could not refund {request.Reward} for marketplace request #{request.Id} to {request.RequesterCharacterName}");
        }

        storage.Requests.Clear();

        foreach (var ((userId, name), amount) in storage.PendingPayouts.ToList())
        {
            if (!TryDepositToSeller(userId, name, amount))
                Log.Warning($"Could not pay out {amount} in pending marketplace earnings to {name}");
        }

        storage.PendingPayouts.Clear();
    }

    #endregion

    #region Offers

    private void OnMakeOffer(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceMakeOfferMessage args)
    {
        var user = args.Actor;
        var comp = ent.Comp;
        var storage = GetStorage();

        if (!storage.Comp.Requests.TryGetValue(args.RequestId, out var request))
        {
            Deny(ent, user, "marketplace-request-error-missing");
            return;
        }

        if (comp.ItemSlot.Item is not { } item || TerminatingOrDeleted(item))
        {
            Deny(ent, user, "marketplace-error-no-item");
            return;
        }

        if (!TryGetSeller(user, out var session, out var profile))
        {
            Deny(ent, user, "marketplace-error-no-account");
            return;
        }

        if (session.UserId == request.RequesterUserId && profile.Name == request.RequesterCharacterName)
        {
            Deny(ent, user, "marketplace-request-error-own");
            return;
        }

        if (GetDenyReason(ent, item) is { } reason)
        {
            Deny(ent, user, reason);
            return;
        }

        var pendingOnRequest = 0;
        var ownOffers = 0;
        foreach (var offer in storage.Comp.Offers.Values)
        {
            if (offer.RequestId == request.Id && offer.Status == MarketplaceOfferStatus.Pending)
                pendingOnRequest++;

            if (offer.OffererUserId == session.UserId && offer.OffererCharacterName == profile.Name)
                ownOffers++;
        }

        if (pendingOnRequest >= comp.MaxOffersPerRequest)
        {
            Deny(ent, user, "marketplace-request-error-too-many-offers", ("max", comp.MaxOffersPerRequest));
            return;
        }

        if (ownOffers >= comp.MaxOffersPerOfferer)
        {
            Deny(ent, user, "marketplace-request-error-too-many-own-offers", ("max", comp.MaxOffersPerOfferer));
            return;
        }

        // Offer only part of a stack if asked to; the rest stays in the console.
        var toOffer = item;
        TryComp<StackComponent>(item, out var stack);
        if (stack != null)
        {
            var available = _stack.GetCount(item, stack);
            var amount = Math.Clamp(args.Amount, 1, available);
            if (amount < available)
            {
                if (_stack.Split(item, amount, Transform(ent).Coordinates, stack) is not { } split)
                {
                    Deny(ent, user, "marketplace-error-failed");
                    return;
                }

                toOffer = split;
            }
        }

        var newOffer = new MarketplaceOffer
        {
            Id = storage.Comp.NextOfferId++,
            RequestId = request.Id,
            RequestTitle = request.Title,
            RequestReward = request.Reward,
            Item = toOffer,
            Name = Name(toOffer),
            ProtoId = Prototype(toOffer)?.ID,
            Stackable = stack != null,
            OffererUserId = session.UserId,
            OffererCharacterName = profile.Name,
            OffererIdName = GetIdName(user, profile),
            GridName = GetGridName(ent),
            Status = MarketplaceOfferStatus.Pending,
        };

        var listed = EnsureComp<MarketplaceListedItemComponent>(toOffer);
        Dirty(toOffer, listed);
        _container.Insert(toOffer, GetStorageContainer(storage));
        storage.Comp.Offers[newOffer.Id] = newOffer;

        _audio.PlayPvs(comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("marketplace-offer-popup-made", ("item", newOffer.Name), ("title", request.Title)), ent, user);

        if (_player.TryGetSessionById(request.RequesterUserId, out var requesterSession)
            && requesterSession.AttachedEntity is { } requesterMob
            && IsCharacter(requesterMob, request.RequesterUserId, request.RequesterCharacterName))
        {
            _popup.PopupEntity(Loc.GetString("marketplace-offer-popup-received", ("item", newOffer.Name), ("title", request.Title)),
                requesterMob,
                requesterMob);
        }

        _adminLog.Add(LogType.ATMUsage,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} offered {ToPrettyString(toOffer):item} for marketplace request #{request.Id} (offer #{newOffer.Id})");

        UpdateAllUis();
    }

    private void OnAcceptOffer(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceAcceptOfferMessage args)
    {
        var user = args.Actor;
        var storage = GetStorage();

        if (!storage.Comp.Offers.TryGetValue(args.OfferId, out var offer)
            || offer.Status != MarketplaceOfferStatus.Pending
            || !storage.Comp.Requests.TryGetValue(offer.RequestId, out var request))
        {
            Deny(ent, user, "marketplace-offer-error-missing");
            return;
        }

        if (!IsCharacter(user, request.RequesterUserId, request.RequesterCharacterName))
        {
            Deny(ent, user, "marketplace-request-error-not-owner");
            return;
        }

        if (TerminatingOrDeleted(offer.Item))
        {
            storage.Comp.Offers.Remove(offer.Id);
            Deny(ent, user, "marketplace-offer-error-missing");
            UpdateAllUis();
            return;
        }

        // The reward was already taken from the requester when the request was made.
        var tax = GetTax(request.Reward, ent.Comp.TaxRate);
        var payout = request.Reward - tax;

        var paidDirectly = payout <= 0 || TryDepositToSeller(offer.OffererUserId, offer.OffererCharacterName, payout);
        if (!paidDirectly)
        {
            var key = (offer.OffererUserId, offer.OffererCharacterName);
            storage.Comp.PendingPayouts[key] = storage.Comp.PendingPayouts.GetValueOrDefault(key) + payout;
        }

        if (tax > 0)
            _bank.TrySectorDeposit(ent.Comp.TaxAccount, tax, LedgerEntryType.MarketplaceTax);

        storage.Comp.Offers.Remove(offer.Id);
        CloseRequest(storage, request);
        GiveItem(storage, offer.Item, ent, user);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("marketplace-offer-popup-accepted", ("item", offer.Name), ("title", request.Title)), ent, user);

        if (_player.TryGetSessionById(offer.OffererUserId, out var offererSession)
            && offererSession.AttachedEntity is { } offererMob
            && IsCharacter(offererMob, offer.OffererUserId, offer.OffererCharacterName))
        {
            _popup.PopupEntity(Loc.GetString(paidDirectly ? "marketplace-offer-popup-paid" : "marketplace-offer-popup-paid-pending",
                    ("title", request.Title),
                    ("payout", payout),
                    ("tax", tax)),
                offererMob,
                offererMob);
        }

        _adminLog.Add(LogType.ATMUsage,
            LogImpact.Low,
            $"{ToPrettyString(user):actor} accepted offer #{offer.Id} from {offer.OffererCharacterName} for marketplace request #{request.Id}, paying {request.Reward} (tax {tax}){(paidDirectly ? "" : ", payout is pending")}");

        UpdateAllUis();
    }

    private void OnRejectOffer(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceRejectOfferMessage args)
    {
        var user = args.Actor;
        var storage = GetStorage();

        if (!storage.Comp.Offers.TryGetValue(args.OfferId, out var offer)
            || offer.Status != MarketplaceOfferStatus.Pending
            || !storage.Comp.Requests.TryGetValue(offer.RequestId, out var request))
        {
            Deny(ent, user, "marketplace-offer-error-missing");
            return;
        }

        if (!IsCharacter(user, request.RequesterUserId, request.RequesterCharacterName))
        {
            Deny(ent, user, "marketplace-request-error-not-owner");
            return;
        }

        offer.Status = MarketplaceOfferStatus.Returned;
        _popup.PopupEntity(Loc.GetString("marketplace-offer-popup-rejected", ("item", offer.Name)), ent, user);
        UpdateAllUis();
    }

    private void OnWithdrawOffer(Entity<MarketplaceConsoleComponent> ent, ref MarketplaceWithdrawOfferMessage args)
    {
        var user = args.Actor;
        var storage = GetStorage();

        if (!storage.Comp.Offers.TryGetValue(args.OfferId, out var offer))
        {
            Deny(ent, user, "marketplace-offer-error-missing");
            return;
        }

        if (!IsCharacter(user, offer.OffererUserId, offer.OffererCharacterName))
        {
            Deny(ent, user, "marketplace-offer-error-not-owner");
            return;
        }

        storage.Comp.Offers.Remove(offer.Id);
        if (!TerminatingOrDeleted(offer.Item))
            GiveItem(storage, offer.Item, ent, user);

        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _popup.PopupEntity(Loc.GetString("marketplace-offer-popup-withdrawn", ("item", offer.Name)), ent, user);
        UpdateAllUis();
    }

    /// <summary>
    /// Takes an offered item out of storage and hands it to the user at the console.
    /// </summary>
    private void GiveItem(Entity<MarketplaceStorageComponent> storage, EntityUid item, EntityUid console, EntityUid user)
    {
        _container.Remove(item, GetStorageContainer(storage), destination: Transform(console).Coordinates);
        RemComp<MarketplaceListedItemComponent>(item);
        _hands.PickupOrDrop(user, item, dropNear: true);
    }

    #endregion

    #region UI

    private void FillRequests(MarketplaceConsoleState state, MarketplaceStorageComponent storage, NetUserId? viewerId, string? viewerName)
    {
        bool IsViewer(NetUserId id, string name) => viewerId == id && viewerName == name;

        var offersByRequest = new Dictionary<int, List<MarketplaceOffer>>();
        foreach (var offer in storage.Offers.Values)
        {
            if (IsViewer(offer.OffererUserId, offer.OffererCharacterName))
                state.MyOffers.Add(ToData(offer));

            if (offer.Status == MarketplaceOfferStatus.Pending)
                offersByRequest.GetOrNew(offer.RequestId).Add(offer);
        }

        foreach (var request in storage.Requests.Values)
        {
            var isOwn = IsViewer(request.RequesterUserId, request.RequesterCharacterName);
            var offers = offersByRequest.GetValueOrDefault(request.Id);

            var data = new MarketplaceRequestData
            {
                Id = request.Id,
                Title = request.Title,
                Description = request.Description,
                Quantity = request.Quantity,
                Reward = request.Reward,
                RequesterName = request.RequesterCharacterName,
                RequesterIdName = request.RequesterIdName,
                GridName = request.GridName,
                CreatedAt = request.CreatedAt,
                IsOwn = isOwn,
                OfferCount = offers?.Count ?? 0,
                HasOwnOffer = offers?.Any(o => IsViewer(o.OffererUserId, o.OffererCharacterName)) ?? false,
            };

            if (isOwn && offers != null)
            {
                foreach (var offer in offers)
                {
                    data.Offers.Add(ToData(offer));
                }
            }

            state.Requests.Add(data);
        }
    }

    private MarketplaceOfferData ToData(MarketplaceOffer offer)
    {
        var exists = !TerminatingOrDeleted(offer.Item);
        return new MarketplaceOfferData
        {
            Id = offer.Id,
            RequestId = offer.RequestId,
            RequestTitle = offer.RequestTitle,
            RequestReward = offer.RequestReward,
            Name = offer.Name,
            ProtoId = offer.ProtoId,
            DisplayEntity = exists ? GetNetEntity(offer.Item) : null,
            Stackable = offer.Stackable,
            Count = exists && offer.Stackable ? _stack.GetCount(offer.Item) : 1,
            OffererName = offer.OffererCharacterName,
            OffererIdName = offer.OffererIdName,
            GridName = offer.GridName,
            Status = offer.Status,
        };
    }

    #endregion
}
