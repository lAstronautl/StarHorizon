using Content.Shared._Horizon.WebConsole;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.WebConsole;

public sealed class WebConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WebConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WebConsoleComponent, WebConsoleOpenUrlMessage>(OnOpenUrl);
        SubscribeLocalEvent<WebConsoleComponent, WebConsoleOpenPageMessage>(OnOpenPage);
    }

    private void OnOpened(Entity<WebConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        var pages = new List<(string, string)>();
        if (ent.Comp.Mode == WebConsoleMode.Whitelist)
        {
            foreach (var page in _proto.EnumeratePrototypes<WebPagePrototype>())
                pages.Add((page.ID, page.Name));
        }

        _ui.SetUiState(ent.Owner, WebConsoleUiKey.Key, new WebConsoleBoundState(ent.Comp.Mode, pages));
    }

    private void OnOpenUrl(Entity<WebConsoleComponent> ent, ref WebConsoleOpenUrlMessage args)
    {
        if (ent.Comp.Mode != WebConsoleMode.Free)
            return;

        if (!WebConsoleUtils.TryValidate(args.Url, out var uri))
            return;

        Send(ent, args.Actor, uri.AbsoluteUri);
    }

    private void OnOpenPage(Entity<WebConsoleComponent> ent, ref WebConsoleOpenPageMessage args)
    {
        if (ent.Comp.Mode != WebConsoleMode.Whitelist)
            return;

        // URL берём только из прототипа, клиентскому не доверяем.
        if (!_proto.TryIndex<WebPagePrototype>(args.PageId, out var page))
            return;

        if (!WebConsoleUtils.TryValidate(page.Url, out var uri))
            return;

        Send(ent, args.Actor, uri.AbsoluteUri);
    }

    private void Send(Entity<WebConsoleComponent> ent, EntityUid user, string url)
    {
        if (_timing.CurTime < ent.Comp.NextUse)
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        ent.Comp.NextUse = _timing.CurTime + ent.Comp.Cooldown;
        Log.Info($"{ToPrettyString(user)} opened {url} via {ToPrettyString(ent)}");
        RaiseNetworkEvent(new OpenWebUrlEvent(url), actor.PlayerSession.Channel);
    }
}
