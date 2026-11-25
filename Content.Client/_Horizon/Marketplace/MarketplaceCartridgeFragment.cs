using System.Linq;
using Content.Shared._Horizon.Marketplace;
using Content.Shared._NF.Bank;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Horizon.Marketplace;

/// <summary>
/// The PDA screen of a marketplace program. One column, no buying or offering - those happen at a terminal.
/// </summary>
public sealed class MarketplaceCartridgeFragment : BoxContainer
{
    public event Action<string, string, int, int>? OnCreateRequest;

    private readonly MarketplaceTheme _theme;
    private readonly RichTextLabel _header;
    private readonly LineEdit _listingSearch;
    private readonly BoxContainer _listings;
    private readonly Label _listingsEmpty;
    private readonly LineEdit _requestSearch;
    private readonly BoxContainer _requests;
    private readonly Label _requestsEmpty;
    private readonly MarketplaceRequestForm _form;

    private MarketplaceCartridgeState? _state;

    public MarketplaceCartridgeFragment(MarketplaceTheme theme)
    {
        _theme = theme;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 6;
        Margin = new Thickness(5);

        var headerPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = theme.HeaderBackground,
                BorderColor = theme.HeaderBorder,
                BorderThickness = new Thickness(0, 0, 0, 2),
            },
        };
        _header = new RichTextLabel { Margin = new Thickness(8, 4, 8, 4) };
        headerPanel.AddChild(_header);
        AddChild(headerPanel);

        var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
        AddChild(tabs);

        (_listingSearch, _listings, _listingsEmpty) = AddListTab(tabs, "marketplace-search-placeholder", "marketplace-empty");
        _listingSearch.OnTextChanged += _ => RebuildListings();

        (_requestSearch, _requests, _requestsEmpty) = AddListTab(tabs, "marketplace-request-search-placeholder", "marketplace-request-empty");
        _requestSearch.OnTextChanged += _ => RebuildRequests();

        var formScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        var formPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = theme.CardBackground,
                BorderColor = theme.CardBorder,
                BorderThickness = new Thickness(1),
            },
        };
        _form = new MarketplaceRequestForm(theme, "marketplace-pda-request-note", descriptionHeight: 70)
        {
            Margin = new Thickness(10, 8, 10, 8),
        };
        _form.OnCreate += (title, description, quantity, reward) => OnCreateRequest?.Invoke(title, description, quantity, reward);
        formPanel.AddChild(_form);
        formScroll.AddChild(formPanel);
        tabs.AddChild(formScroll);

        tabs.SetTabTitle(0, Loc.GetString("marketplace-tab-market"));
        tabs.SetTabTitle(1, Loc.GetString("marketplace-tab-requests"));
        tabs.SetTabTitle(2, Loc.GetString("marketplace-pda-tab-new-request"));

        UpdateHeader();
    }

    private (LineEdit, BoxContainer, Label) AddListTab(TabContainer tabs, string placeholderKey, string emptyKey)
    {
        var page = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(0, 4, 0, 0) };
        var search = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString(placeholderKey) };
        page.AddChild(search);

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(0, 0, 6, 0) };
        var list = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
        var empty = new Label
        {
            Text = Loc.GetString(emptyKey),
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 30, 0, 30),
            StyleClasses = { "LabelSubText" },
            Visible = false,
        };
        content.AddChild(list);
        content.AddChild(empty);
        scroll.AddChild(content);
        page.AddChild(scroll);
        tabs.AddChild(page);
        return (search, list, empty);
    }

    public void UpdateState(MarketplaceCartridgeState state)
    {
        _state = state;
        UpdateHeader();
        RebuildListings();
        RebuildRequests();
        _form.SetLimits(state.TaxRate,
            state.Balance,
            state.HasAccount,
            state.MaxReward,
            state.Requests.Count(r => r.IsOwn) >= state.MaxRequestsPerPlayer);
    }

    private void UpdateHeader()
    {
        var message = new FormattedMessage();
        message.AddMarkupPermissive(Loc.GetString(_theme.HeaderTitleKey));
        message.PushNewline();
        message.AddMarkupPermissive(Loc.GetString("marketplace-pda-subtitle",
            ("tax", _state == null ? 0 : (int) MathF.Round(_state.TaxRate * 100f)),
            ("listings", _state?.Listings.Count ?? 0),
            ("requests", _state?.Requests.Count ?? 0),
            ("balance", BankSystemExtensions.ToSpesoString(_state?.Balance ?? 0)),
            ("mutedColor", _theme.Muted),
            ("priceColor", _theme.Price)));
        _header.SetMessage(message);
    }

    private static bool Matches(string search, params string[] fields)
    {
        return fields.Any(f => f.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private void RebuildListings()
    {
        _listings.RemoveAllChildren();
        if (_state == null)
            return;

        var search = _listingSearch.Text.Trim();
        var any = false;
        foreach (var listing in _state.Listings
                     .Where(l => search.Length == 0 || Matches(search, l.Name, l.SellerName, l.GridName, $"#{l.Id}"))
                     .OrderByDescending(l => l.ListedAt)
                     .ThenByDescending(l => l.Id))
        {
            _listings.AddChild(new MarketplaceListingCard(_theme, listing, _state.Balance, _state.HasAccount, readOnly: true));
            any = true;
        }

        _listingsEmpty.Visible = !any;
        _listingsEmpty.Text = Loc.GetString(_state.Listings.Count == 0 ? "marketplace-empty" : "marketplace-empty-search");
    }

    private void RebuildRequests()
    {
        _requests.RemoveAllChildren();
        if (_state == null)
            return;

        var search = _requestSearch.Text.Trim();
        var any = false;
        foreach (var request in _state.Requests
                     .Where(r => search.Length == 0 || Matches(search, r.Title, r.Description, r.RequesterName, r.GridName, $"#{r.Id}"))
                     .OrderByDescending(r => r.IsOwn)
                     .ThenByDescending(r => r.CreatedAt)
                     .ThenByDescending(r => r.Id))
        {
            _requests.AddChild(new MarketplaceRequestCard(_theme, request, null, 0, null, 0, readOnly: true));
            any = true;
        }

        _requestsEmpty.Visible = !any;
        _requestsEmpty.Text = Loc.GetString(_state.Requests.Count == 0 ? "marketplace-request-empty" : "marketplace-empty-search");
    }
}
