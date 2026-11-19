using System.Numerics;
using Content.Shared._Horizon.WebConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.WebView;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Horizon.WebConsole;

/// <summary>
///     Встроенный браузер (CEF). Этот класс нельзя создавать, если модуль Robust.Client.WebView не загружен:
///     проверку делает <see cref="WebConsoleWindow"/>.
/// </summary>
public sealed class WebConsoleBrowserPanel : BoxContainer
{
    private readonly WebViewControl _view;
    private readonly Label _status = new();
    private readonly ISawmill _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("webconsole");
    private List<string>? _allowedHosts;
    private bool _closed;
    private bool _navigated;
    private bool _nudged;
    private bool _lastLoading;
    private string _currentUrl = string.Empty;
    private float _sinceNavigate;

    public WebConsoleBrowserPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        VerticalExpand = true;
        HorizontalExpand = true;

        // Символы вроде ⟳ отсутствуют в шрифте игры, поэтому только текст.
        var back = new Button { Text = Loc.GetString("web-console-back") };
        var forward = new Button { Text = Loc.GetString("web-console-forward") };
        var reload = new Button { Text = Loc.GetString("web-console-reload") };
        back.OnPressed += _ => { if (!_closed) _view!.GoBack(); };
        forward.OnPressed += _ => { if (!_closed) _view!.GoForward(); };
        reload.OnPressed += _ => { if (!_closed) _view!.Reload(); };

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        bar.AddChild(back);
        bar.AddChild(forward);
        bar.AddChild(reload);
        bar.AddChild(_status);
        AddChild(bar);

        _view = new WebViewControl
        {
            MinSize = new(780, 500),
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _view.AddBeforeBrowseHandler(OnBeforeBrowse);
        _view.AddResourceRequestHandler(OnResourceRequest);
        AddChild(_view);

        WebConsoleLifetime.Register(this, ShutdownClose);
    }

    /// <summary>null — любые http(s) адреса, иначе только перечисленные хосты.</summary>
    public void SetAllowedHosts(List<string>? hosts)
    {
        _allowedHosts = hosts;
    }

    public void Navigate(string url)
    {
        if (_closed)
            return;

        _sawmill.Info($"Navigate: {url}");
        _currentUrl = url;
        _navigated = true;
        _nudged = false;
        _sinceNavigate = 0;
        _view.Url = url;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_closed)
            return;

        var loading = _view.IsLoading;
        if (loading != _lastLoading)
        {
            _lastLoading = loading;
            _sawmill.Info($"Loading state: {(loading ? "loading" : "done")} ({_currentUrl})");
        }

        if (_navigated)
        {
            _sinceNavigate += args.DeltaSeconds;
            _status.Text = loading
                ? Loc.GetString("web-console-status-loading", ("url", _currentUrl))
                : Loc.GetString("web-console-status-ready", ("url", _currentUrl));

            // Обход для стандартного WebView: CEF не узнаёт размер области, пока она не изменится.
            // Один раз после загрузки (или через 3 секунды) растягиваем на 1px и возвращаем обратно.
            if (!_nudged && ((!loading && _sinceNavigate >= 1f) || _sinceNavigate >= 3f))
            {
                _nudged = true;
                _view.Arrange(UIBox2.FromDimensions(Vector2.Zero, _view.Size + Vector2.One));
                InvalidateArrange();
                _sawmill.Info("Viewport nudge applied");
            }
        }
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        // Сам WebViewControl закрывает браузер при выходе из дерева; здесь только снимаем учёт и блокируем JS/навигацию.
        MarkClosed();
    }

    private void MarkClosed()
    {
        if (_closed)
            return;

        _closed = true;
        WebConsoleLifetime.Unregister(this);
        _view.AlwaysActive = false;
    }

    /// <summary>Закрытие перед остановкой движка, пока CEF ещё жив.</summary>
    private void ShutdownClose()
    {
        MarkClosed();
        if (_view.Disposed)
            return;

        _view.ReleaseKeyboardFocus();
        if (_view.IsInsideTree && _view.Parent is { IsInsideTree: false })
        {
            // Предок уже вышел из дерева: убираем браузер отложенно, иначе ExitedTree не вызовется.
            _view.UserInterfaceManager.DeferAction(() =>
            {
                if (!_view.Disposed)
                    _view.Orphan();
            });
            return;
        }

        _view.Orphan();
    }

    private bool HostAllowed(string url)
    {
        var hosts = _allowedHosts;
        return hosts == null || hosts.Contains(WebConsoleUtils.GetHost(url));
    }

    private void OnBeforeBrowse(IBeforeBrowseContext ctx)
    {
        // about:blank — служебная начальная страница браузера, её отменять нельзя.
        if (ctx.Url == "about:blank")
            return;

        if (_closed)
        {
            ctx.DoCancel();
            return;
        }

        if (!WebConsoleUtils.TryValidate(ctx.Url, out var url))
        {
            _sawmill.Warning($"Blocked navigation (invalid url): {ctx.Url}");
            ctx.DoCancel();
            return;
        }

        if (!HostAllowed(url))
        {
            _sawmill.Warning($"Blocked navigation (host not allowed): {url}");
            ctx.DoCancel();
        }
    }

    private void OnResourceRequest(IRequestHandlerContext ctx)
    {
        if (_closed || ctx.IsDownload)
        {
            ctx.DoCancel();
            return;
        }

        // Встроенные data: ресурсы (шрифты, картинки) сети не касаются.
        if (!ctx.IsNavigation && ctx.Url.StartsWith("data:", StringComparison.Ordinal))
            return;

        if (ctx.Url == "about:blank")
            return;

        if (!WebConsoleUtils.TryValidate(ctx.Url, out var url))
        {
            ctx.DoCancel();
            return;
        }

        // В ограниченном режиме строго: только GET и только разрешённые хосты (включая подресурсы).
        if (_allowedHosts != null && (ctx.Method != "GET" || !HostAllowed(url)))
            ctx.DoCancel();
    }
}
