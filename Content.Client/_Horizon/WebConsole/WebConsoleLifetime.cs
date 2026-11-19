using System.Linq;

namespace Content.Client._Horizon.WebConsole;

/// <summary>
///     Реестр открытых браузеров консолей. Нужен, чтобы закрыть их до остановки движка:
///     CEF владеет буферами до закрытия браузера, а после остановки WebView трогать их нельзя.
///     Намеренно не ссылается на типы WebView, чтобы класс грузился и без модуля.
/// </summary>
public static class WebConsoleLifetime
{
    private static readonly Dictionary<object, Action> Active = new();

    public static bool IsShuttingDown { get; private set; }

    public static void Register(object key, Action close)
    {
        Active[key] = close;
    }

    public static void Unregister(object key)
    {
        Active.Remove(key);
    }

    /// <summary>Вызывается из EntryPoint.Shutdown, пока CEF ещё жив.</summary>
    public static void Shutdown()
    {
        IsShuttingDown = true;
        foreach (var close in Active.Values.ToArray())
        {
            close();
        }

        Active.Clear();
    }
}
