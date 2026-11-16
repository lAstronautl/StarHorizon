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

    // Без System.Uri: UriKind и UriFormatException не входят в белый список песочницы.
    public static bool TryValidate(string? raw, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim();
        if (s.Length > MaxUrlLength)
            return false;

        string rest;
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            rest = s.Substring(8);
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            rest = s.Substring(7);
        else
            return false;

        foreach (var c in s)
        {
            if (c <= ' ' || c == '\\' || c == '<' || c == '>' || c == '"' || c > '~')
                return false;
        }

        var end = rest.IndexOfAny(new[] { '/', '?', '#' });
        var authority = end < 0 ? rest : rest.Substring(0, end);

        // Пустой хост или логин:пароль@ недопустимы.
        if (authority.Length == 0 || authority.Contains('@') || authority[0] == ':' || authority[0] == '.')
            return false;

        url = s;
        return true;
    }
}
