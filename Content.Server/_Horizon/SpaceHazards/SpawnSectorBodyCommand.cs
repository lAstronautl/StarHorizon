using System.Numerics;
using Content.Server.Administration;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._Horizon.SpaceHazards;

/// <summary>
/// Spawns a sector star, black hole or background planet on the caller's map, for testing and mapping.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed class SpawnSectorBodyCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "spawnsectorbody";
    public string Description => "Spawns a sector star, black hole or background planet.";
    public string Help => "spawnsectorbody <star|blackhole|planet> [x] [y] [planetKind 0-7]\n" +
                          "Without coordinates it spawns at your position. Planet kinds: " +
                          "0 LandRivers, 1 TerranDry, 2 LandMasses, 3 NoAtmosphere, 4 GasPlanet, " +
                          "5 GasPlanetLayers, 6 IceWorld, 7 LavaWorld (omit for random).";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 4 || args.Length == 2)
        {
            shell.WriteError(Help);
            return;
        }

        var proto = args[0].ToLowerInvariant() switch
        {
            "star" => "SectorCelestialStar",
            "blackhole" => "SectorCelestialBlackHole",
            "planet" => "SectorBackgroundPlanet",
            _ => null,
        };

        if (proto == null)
        {
            shell.WriteError($"Unknown type '{args[0]}'. Use star, blackhole or planet.");
            return;
        }

        var xformSys = _entMan.System<SharedTransformSystem>();
        MapId mapId;
        Vector2 pos;

        if (args.Length >= 3)
        {
            if (!float.TryParse(args[1], out var x) || !float.TryParse(args[2], out var y))
            {
                shell.WriteError("x and y must be numbers.");
                return;
            }

            if (shell.Player?.AttachedEntity is not { } attached)
            {
                shell.WriteError("You need an attached entity so the map can be determined.");
                return;
            }

            mapId = _entMan.GetComponent<TransformComponent>(attached).MapID;
            pos = new Vector2(x, y);
        }
        else
        {
            if (shell.Player?.AttachedEntity is not { } attached)
            {
                shell.WriteError("You need an attached entity.");
                return;
            }

            var xform = _entMan.GetComponent<TransformComponent>(attached);
            mapId = xform.MapID;
            pos = xformSys.GetWorldPosition(xform);
        }

        if (mapId == MapId.Nullspace)
        {
            shell.WriteError("You must be on a map.");
            return;
        }

        var ent = _entMan.SpawnEntity(proto, new MapCoordinates(pos, mapId));

        if (args.Length == 4 && _entMan.TryGetComponent(ent, out SectorBackgroundPlanetComponent? planet))
        {
            if (!byte.TryParse(args[3], out var kind) || kind > (byte) PixelPlanetKind.LavaWorld)
            {
                shell.WriteError("planetKind must be 0-7; spawned with a random kind.");
            }
            else
            {
                planet.PlanetKind = (PixelPlanetKind) kind;
                _entMan.Dirty(ent, planet);
            }
        }

        shell.WriteLine($"Spawned {proto} ({ent}) at {pos} on map {mapId}.");
    }
}
