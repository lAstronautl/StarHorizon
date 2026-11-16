using Content.Shared._Horizon.WebConsole;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;

namespace Content.Client._Horizon.WebConsole;

[UsedImplicitly]
public sealed class WebConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private WebConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WebConsoleWindow>();
        _window.OnOpenUrl += url => SendMessage(new WebConsoleOpenUrlMessage(url));
        _window.OnOpenPage += id => SendMessage(new WebConsoleOpenPageMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WebConsoleBoundState s)
            _window?.Populate(s);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WebConsoleNavigateMessage nav)
            _window?.Navigate(nav.Url);
    }
}

public sealed class WebConsoleWindow : DefaultWindow
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IUriOpener _uriOpener = default!;

    public event Action<string>? OnOpenUrl;
    public event Action<string>? OnOpenPage;

    private readonly BoxContainer _root = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 6,
        VerticalExpand = true,
    };

    private WebConsoleBrowserPanel? _browser;
    private bool _built;

    public WebConsoleWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("web-console-title");
        MinSize = new(420, 160);
        Contents.AddChild(_root);
    }

    /// <summary>
    ///     Модуль WebView грузится только если он указан в manifest.yml и у клиента есть CEF.
    ///     Его CVar'ы регистрируются вместе с модулем, поэтому по ним и определяем доступность.
    /// </summary>
    private bool WebViewAvailable()
    {
        return _cfg.IsCVarRegistered("web.headless") && !_cfg.GetCVar<bool>("web.headless");
    }

    public void Populate(WebConsoleBoundState state)
    {
        if (_built)
            return;
        _built = true;

        if (!state.Enabled)
        {
            _root.AddChild(new Label { Text = Loc.GetString("web-console-disabled") });
            return;
        }

        var hasBrowser = WebViewAvailable();

        if (hasBrowser)
        {
            MinSize = new(820, 640);
            _browser = BuildBrowser(state);
        }
        else
        {
            _root.AddChild(new Label { Text = Loc.GetString("web-console-no-cef") });
        }

        if (state.Mode == WebConsoleMode.Free)
        {
            var edit = new LineEdit { PlaceHolder = "https://", HorizontalExpand = true };
            var button = new Button { Text = Loc.GetString("web-console-open") };
            void Submit() => OnOpenUrl?.Invoke(edit.Text);
            button.OnPressed += _ => Submit();
            edit.OnTextEntered += _ => Submit();

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            row.AddChild(edit);
            row.AddChild(button);
            _root.AddChild(new Label { Text = Loc.GetString("web-console-enter-url") });
            _root.AddChild(row);
        }
        else
        {
            if (state.Pages.Count == 0)
                _root.AddChild(new Label { Text = Loc.GetString("web-console-no-pages") });

            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            foreach (var (id, name) in state.Pages)
            {
                var pageId = id;
                var button = new Button { Text = name, HorizontalExpand = true };
                button.OnPressed += _ => OnOpenPage?.Invoke(pageId);
                row.AddChild(button);
            }
            _root.AddChild(row);
        }

        if (_browser != null)
            _root.AddChild(_browser);
    }

    // Отдельный метод: класс браузера загружается только когда модуль доступен.
    private WebConsoleBrowserPanel BuildBrowser(WebConsoleBoundState state)
    {
        var panel = new WebConsoleBrowserPanel();
        panel.SetAllowedHosts(state.Mode == WebConsoleMode.Whitelist ? state.AllowedHosts : null);
        return panel;
    }

    public void Navigate(string url)
    {
        if (_browser != null)
            _browser.Navigate(url);
        else
            _uriOpener.OpenUri(url); // запасной путь: внешний браузер
    }
}
