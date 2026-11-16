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

    public WebConsoleBrowserPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        VerticalExpand = true;
        HorizontalExpand = true;

        var back = new Button { Text = "◀" };
        var forward = new Button { Text = "▶" };
        var reload = new Button { Text = "⟳" };
        back.OnPressed += _ => _view!.GoBack();
        forward.OnPressed += _ => _view!.GoForward();
        reload.OnPressed += _ => _view!.Reload();

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
        AddChild(_view);
    }

    /// <summary>null — любые http(s) адреса, иначе только перечисленные хосты.</summary>
    public void SetAllowedHosts(List<string>? hosts)
    {
        _allowedHosts = hosts;
    }

    public void Navigate(string url)
    {
        _view.Url = url;
    }

    private void OnBeforeBrowse(IBeforeBrowseContext ctx)
    {
        if (!WebConsoleUtils.TryValidate(ctx.Url, out var url))
        {
            ctx.DoCancel();
            return;
        }

        var hosts = _allowedHosts;
        if (hosts != null && !hosts.Contains(WebConsoleUtils.GetHost(url)))
            ctx.DoCancel();
    }
}
