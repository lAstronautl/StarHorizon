using System.Linq;
using Robust.Shared.Console;

namespace Content.Client._Mono.SaigaAgent;

/// <summary>
///     Console controls for the client-side Saiga AI agent.
///     Usage: saiga_agent on | off | goal &lt;text&gt; | look
/// </summary>
public sealed class SaigaAgentCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "saiga_agent";
    public string Description => "Управление клиентским ИИ-агентом «Сайга», который пилотит твоего персонажа.";
    public string Help => "Использование: saiga_agent <on|off|join|goal <текст>>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var sys = _entMan.System<SaigaAgentSystem>();

        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            shell.WriteLine($"Сейчас: {(sys.Enabled ? "включён" : "выключен")}. Цель: {sys.Goal}");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                sys.SetEnabled(true);
                shell.WriteLine("Агент включён. Говори рядом с персонажем — он отреагирует сразу.");
                break;

            case "off":
                sys.SetEnabled(false);
                shell.WriteLine("Агент выключен.");
                break;

            case "join":
                shell.WriteLine(sys.TryJoin());
                break;

            case "goal":
                if (args.Length < 2)
                {
                    shell.WriteLine("Укажи цель: saiga_agent goal <текст>");
                    return;
                }
                sys.SetGoal(string.Join(" ", args.Skip(1)));
                shell.WriteLine($"Новая цель: {sys.Goal}");
                break;

            default:
                shell.WriteLine(Help);
                break;
        }
    }
}
