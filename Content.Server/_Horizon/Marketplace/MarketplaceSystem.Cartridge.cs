using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Shared._Horizon.Marketplace;
using Content.Shared.CartridgeLoader;

namespace Content.Server._Horizon.Marketplace;

/// <summary>
/// PDA programs for a marketplace network: browse listings and requests, and post new requests.
/// Buying, selling and handling offers stay at the terminals.
/// </summary>
public sealed partial class MarketplaceSystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;

    private void InitializeCartridge()
    {
        SubscribeLocalEvent<MarketplaceCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeUiReady);
        SubscribeLocalEvent<MarketplaceCartridgeComponent, CartridgeMessageEvent>(OnCartridgeMessage);
    }

    private void OnCartridgeUiReady(Entity<MarketplaceCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateCartridge(ent, args.Loader);
    }

    private void OnCartridgeMessage(Entity<MarketplaceCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not MarketplaceCartridgeCreateRequestEvent create)
            return;

        var user = args.Actor;
        var loader = GetEntity(args.LoaderUid);
        var gridName = GetGridName(user);

        if (!TryCreateRequest(user,
                ent.Comp.Network,
                ent.Comp.MaxReward,
                ent.Comp.MaxRequestsPerRequester,
                gridName,
                create.Title,
                create.Description,
                create.Quantity,
                create.Reward,
                out var error))
        {
            _popup.PopupEntity(Loc.GetString(error.Value.Message, error.Value.Args), user, user);
        }

        // Balance changed (or the form needs re-enabling), so refresh this PDA even if nothing else did.
        UpdateCartridge(ent, loader);
    }

    /// <summary>
    /// Refreshes every PDA currently showing a program for this network.
    /// </summary>
    private void UpdateCartridges(string network)
    {
        var query = EntityQueryEnumerator<MarketplaceCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var comp, out var cartridge))
        {
            if (comp.Network == network && cartridge.LoaderUid is { } loader)
                UpdateCartridge((uid, comp), loader);
        }
    }

    private void UpdateCartridge(Entity<MarketplaceCartridgeComponent> ent, EntityUid loader)
    {
        // The PDA UI state is shared by all programs, so only push ours while it is the one on screen.
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
            || loaderComp.ActiveProgram != ent.Owner
            || !_ui.IsUiOpen(loader, loaderComp.UiKey))
        {
            return;
        }

        // A PDA is normally only looked at by whoever holds it; personalise the state for them.
        var viewer = _ui.GetActors(loader, loaderComp.UiKey).FirstOrDefault();
        Robust.Shared.Network.NetUserId? viewerId = null;
        string? viewerName = null;
        if (viewer.Valid && TryGetSeller(viewer, out var session, out var profile))
        {
            viewerId = session.UserId;
            viewerName = profile.Name;
        }

        var state = new MarketplaceCartridgeState
        {
            TaxRate = ent.Comp.TaxRate,
            MaxReward = ent.Comp.MaxReward,
            MaxRequestsPerPlayer = ent.Comp.MaxRequestsPerRequester,
        };

        if (viewer.Valid)
            state.HasAccount = _bank.TryGetBalance(viewer, out state.Balance);

        if (TryGetExistingStorage(ent.Comp.Network, out var storage))
        {
            foreach (var listing in storage.Listings.Values)
            {
                state.Listings.Add(ToData(listing,
                    storage,
                    ent.Comp.Anonymous,
                    viewerId == listing.SellerUserId && viewerName == listing.SellerCharacterName,
                    includeEntity: false));
            }

            var offerCounts = new Dictionary<int, int>();
            foreach (var offer in storage.Offers.Values)
            {
                if (offer.Status == MarketplaceOfferStatus.Pending)
                    offerCounts[offer.RequestId] = offerCounts.GetValueOrDefault(offer.RequestId) + 1;
            }

            foreach (var request in storage.Requests.Values)
            {
                state.Requests.Add(new MarketplaceRequestData
                {
                    Id = request.Id,
                    Title = request.Title,
                    Description = request.Description,
                    Quantity = request.Quantity,
                    Reward = request.Reward,
                    RequesterName = DisplayName(ent.Comp.Anonymous, storage, request.RequesterUserId, request.RequesterCharacterName),
                    RequesterIdName = DisplayIdName(ent.Comp.Anonymous, request.RequesterIdName),
                    GridName = DisplayGrid(ent.Comp.Anonymous, storage, request.GridName),
                    CreatedAt = request.CreatedAt,
                    IsOwn = viewerId == request.RequesterUserId && viewerName == request.RequesterCharacterName,
                    OfferCount = offerCounts.GetValueOrDefault(request.Id),
                });
            }
        }

        _cartridgeLoader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
    }
}
