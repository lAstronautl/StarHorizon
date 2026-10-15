using Content.Server.Administration;
using Content.Server._Horizon.Shuttles.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Horizon.Shuttles.Commands;

/// <summary>
/// Debug commands: land / take off the landable shuttle grid the caller is standing on.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed class LandShuttleCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "landshuttle";
    public string Description => "Lands the shuttle you are on onto the landing pad it overlaps.";
    public string Help => $"{Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player ||
            _entManager.GetComponent<TransformComponent>(player).GridUid is not { } grid)
        {
            shell.WriteError("You must be standing on a shuttle.");
            return;
        }

        var system = _entManager.System<ShuttleLandingSystem>();
        shell.WriteLine(system.TryLand(grid, out var reason) ? "Landed." : $"Can't land: {reason}");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class TakeOffShuttleCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "takeoffshuttle";
    public string Description => "Takes off the landed shuttle you are on.";
    public string Help => $"{Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player ||
            _entManager.GetComponent<TransformComponent>(player).GridUid is not { } grid)
        {
            shell.WriteError("You must be standing on a shuttle.");
            return;
        }

        var system = _entManager.System<ShuttleLandingSystem>();
        shell.WriteLine(system.TryTakeOff(grid) ? "Took off." : "Can't take off.");
    }
}
