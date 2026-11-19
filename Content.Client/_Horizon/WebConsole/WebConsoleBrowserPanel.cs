using Content.Shared._Horizon.WebConsole;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.WebView;

namespace Content.Client._Horizon.WebConsole;

/// <summary>
///     Встроенный браузер (CEF). Этот класс нельзя создавать, если модуль Robust.Client.WebView не загружен:
///     проверку делает <see cref="WebConsoleWindow"/>.
/// </summary>
public sealed class WebConsoleBrowserPanel : BoxContainer
{
    private readonly WebViewControl _view;
    private List<string>? _allowedHosts;
    private bool _closed;

    public WebConsoleBrowserPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        VerticalExpand = true;
        HorizontalExpand = true;

        var back = new Button { Text = "◀" };
        var forward = new Button { Text = "▶" };
        var reload = new Button { Text = "⟳" };
        back.OnPressed += _ => { if (!_closed) _view!.GoBack(); };
        forward.OnPressed += _ => { if (!_closed) _view!.GoForward(); };
        reload.OnPressed += _ => { if (!_closed) _view!.Reload(); };

        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        bar.AddChild(back);
        bar.AddChild(forward);
        bar.AddChild(reload);
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

        _view.Url = url;
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
        if (_closed || !WebConsoleUtils.TryValidate(ctx.Url, out var url) || !HostAllowed(url))
            ctx.DoCancel();
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
