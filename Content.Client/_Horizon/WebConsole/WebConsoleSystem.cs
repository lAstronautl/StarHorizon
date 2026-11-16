using Content.Shared._Horizon.WebConsole;
using Robust.Client.UserInterface;

namespace Content.Client._Horizon.WebConsole;

public sealed class WebConsoleSystem : EntitySystem
{
    [Dependency] private readonly IUriOpener _uriOpener = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<OpenWebUrlEvent>(OnOpenUrl);
    }

    private void OnOpenUrl(OpenWebUrlEvent ev)
    {
        // Повторная проверка схемы на клиенте.
        if (WebConsoleUtils.TryValidate(ev.Url, out var url))
            _uriOpener.OpenUri(url);
    }
}
