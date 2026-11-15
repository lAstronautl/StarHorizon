using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.WebConsole;

[Serializable, NetSerializable]
public enum WebConsoleUiKey : byte
{
    Key
}

public enum WebConsoleMode : byte
{
    /// <summary>Игрок вводит собственный URL.</summary>
    Free,
    /// <summary>Только страницы из прототипов <see cref="WebPagePrototype"/>.</summary>
    Whitelist
}

/// <summary>Консоль, открывающая веб-страницу во внешнем браузере игрока.</summary>
[RegisterComponent]
public sealed partial class WebConsoleComponent : Component
{
    [DataField]
    public WebConsoleMode Mode = WebConsoleMode.Free;

    /// <summary>Минимальная задержка между открытиями ссылок.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    [ViewVariables]
    public TimeSpan NextUse;
}

/// <summary>Разрешённая страница для консоли в режиме Whitelist.</summary>
[Prototype("webPage")]
public sealed partial class WebPagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public string Url = string.Empty;
}

[Serializable, NetSerializable]
public sealed class WebConsoleOpenUrlMessage(string url) : BoundUserInterfaceMessage
{
    public readonly string Url = url;
}

[Serializable, NetSerializable]
public sealed class WebConsoleOpenPageMessage(string pageId) : BoundUserInterfaceMessage
{
    public readonly string PageId = pageId;
}

[Serializable, NetSerializable]
public sealed class WebConsoleBoundState(WebConsoleMode mode, List<(string Id, string Name)> pages)
    : BoundUserInterfaceState
{
    public readonly WebConsoleMode Mode = mode;
    public readonly List<(string Id, string Name)> Pages = pages;
}

/// <summary>Сервер → клиент: открыть URL во внешнем браузере.</summary>
[Serializable, NetSerializable]
public sealed class OpenWebUrlEvent(string url) : EntityEventArgs
{
    public readonly string Url = url;
}

public static class WebConsoleUtils
{
    public const int MaxUrlLength = 512;

    public static bool TryValidate(string? raw, out Uri uri)
    {
        uri = default!;
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxUrlLength)
            return false;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!string.IsNullOrEmpty(parsed.UserInfo) || string.IsNullOrEmpty(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }
}
