using Content.Shared._Horizon.WebConsole;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

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
}

public sealed class WebConsoleWindow : DefaultWindow
{
    public event Action<string>? OnOpenUrl;
    public event Action<string>? OnOpenPage;

    private readonly BoxContainer _root = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };

    public WebConsoleWindow()
    {
        Title = Loc.GetString("web-console-title");
        MinSize = new(420, 160);
        Contents.AddChild(_root);
    }

    public void Populate(WebConsoleBoundState state)
    {
        _root.RemoveAllChildren();

        if (state.Mode == WebConsoleMode.Free)
        {
            var edit = new LineEdit { PlaceHolder = "https://", HorizontalExpand = true };
            var button = new Button { Text = Loc.GetString("web-console-open") };
            void Submit() => OnOpenUrl?.Invoke(edit.Text);
            button.OnPressed += _ => Submit();
            edit.OnTextEntered += _ => Submit();
            _root.AddChild(new Label { Text = Loc.GetString("web-console-enter-url") });
            _root.AddChild(edit);
            _root.AddChild(button);
            return;
        }

        if (state.Pages.Count == 0)
            _root.AddChild(new Label { Text = Loc.GetString("web-console-no-pages") });

        foreach (var (id, name) in state.Pages)
        {
            var pageId = id;
            var button = new Button { Text = name, HorizontalExpand = true };
            button.OnPressed += _ => OnOpenPage?.Invoke(pageId);
            _root.AddChild(button);
        }
    }
}
