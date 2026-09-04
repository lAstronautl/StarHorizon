using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Mono.Saiga.Commands;

/// <summary>
///     Sends a prompt to the local Saiga model and prints the reply. For testing the integration.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class SaigaCommand : IConsoleCommand
{
    [Dependency] private readonly SaigaManager _saiga = default!;

    public string Command => "saiga";
    public string Description => "Sends a prompt to the local Saiga (Ollama) model and prints the reply.";
    public string Help => $"Usage: {Command} <prompt...>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine($"Not enough arguments.\n{Help}");
            return;
        }

        if (!_saiga.Enabled)
        {
            shell.WriteError("Saiga is disabled. Set the 'saiga.enabled' CVar to true.");
            return;
        }

        // argStr starts with the command name; strip it to get the raw prompt.
        var prompt = argStr.Substring(Command.Length).Trim();

        shell.WriteLine("Asking Saiga...");

        // Fire the request and write the reply back when it completes.
        _ = RunAsync(shell, prompt);
    }

    private async Task RunAsync(IConsoleShell shell, string prompt)
    {
        var reply = await _saiga.ChatAsync(prompt);
        shell.WriteLine(reply is null
            ? "Saiga request failed (see server log for details)."
            : $"Saiga: {reply}");
    }
}
