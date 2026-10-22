"""Pipe auto-fitting: which SS14 prototype and facing a pipe tile gets, worked
out from its neighbors.

Direct port of the reference editor's src/algorithms/pipeFittings.ts (see
https://github.com/lAstronautl/StarHorizon-Map-Editor), so a converted map's
pipe runs come out exactly as if a mapper had drawn the same layout by hand
with that tool's pipe-draw tool. tg's own per-instance geometry (a "smart
pipe"'s `dir`) is not used for this -- the game recomputes it live from actual
node connections, so what is saved to a .dmm is engine state rather than
authorial intent, and is not safe to decode directly (see the dmm2yml.py
docstring on GAS_PIPE_PATH for the full reasoning). Neighbor analysis, exactly
as the editor does it, is the reliable source of truth here.
"""

from __future__ import annotations

from dataclasses import dataclass

NORTH, SOUTH, EAST, WEST = 1, 2, 4, 8


@dataclass(frozen=True)
class GasProtoFamily:
    straight: str
    bend: str
    t_junction: str
    fourway: str


# The three overlapping gas pipe runs this fork's prototypes support
# (AtmosPipeLayer Primary/Secondary/Tertiary) -- Alt1/Alt2 bake the layer
# into a distinct sprite/drawdepth, same as the reference editor relies on.
GAS_PROTOTYPES: dict[str, GasProtoFamily] = {
    "Primary": GasProtoFamily("GasPipeStraight", "GasPipeBend", "GasPipeTJunction", "GasPipeFourway"),
    "Secondary": GasProtoFamily(
        "GasPipeStraightAlt1", "GasPipeBendAlt1", "GasPipeTJunctionAlt1", "GasPipeFourwayAlt1"
    ),
    "Tertiary": GasProtoFamily(
        "GasPipeStraightAlt2", "GasPipeBendAlt2", "GasPipeTJunctionAlt2", "GasPipeFourwayAlt2"
    ),
}

DISPOSAL_PROTOTYPES = GasProtoFamily("DisposalPipe", "DisposalBend", "DisposalJunction", "DisposalJunction")


@dataclass
class FittedPipe:
    x: int
    y: int
    prototype: str
    facing: int  # NORTH/SOUTH/EAST/WEST in this converter's own dir convention
    color: str | None = None
    pipe_layer: str | None = None  # "Secondary" | "Tertiary", only for a non-Primary gas layer


def _has_neighbor(tiles: set[tuple[int, int]], device_ports: dict[tuple[int, int], set[str]],
                   x: int, y: int, opposite: str) -> bool:
    if (x, y) in tiles:
        return True
    ports = device_ports.get((x, y))
    return bool(ports) and opposite in ports


def fit_pipes(
    tiles: set[tuple[int, int]],
    family: str = "gas",
    color: str | None = None,
    layer: str = "Primary",
    device_ports: dict[tuple[int, int], set[str]] | None = None,
) -> list[FittedPipe]:
    """Compute the fitted prototype+facing for every tile in `tiles`.

    `tiles` must already be restricted to one independent run -- same pipe
    family, same layer, and for gas, the same color -- so up to 3 overlapping
    gas runs on the same tiles never fit against each other.

    `device_ports` maps a (same-network device's) tile position to the set of
    world-cardinal strings ('N'/'S'/'E'/'W') its open port(s) face, so a pipe
    bends toward a vent/pump/etc. the same way it would toward another pipe.
    """
    device_ports = device_ports or {}
    protos = GAS_PROTOTYPES[layer] if family == "gas" else DISPOSAL_PROTOTYPES
    pipe_layer = layer if family == "gas" and layer != "Primary" else None

    results: list[FittedPipe] = []
    for (x, y) in tiles:
        has_n = _has_neighbor(tiles, device_ports, x, y + 1, "S")
        has_s = _has_neighbor(tiles, device_ports, x, y - 1, "N")
        has_e = _has_neighbor(tiles, device_ports, x + 1, y, "W")
        has_w = _has_neighbor(tiles, device_ports, x - 1, y, "E")
        count = has_n + has_s + has_e + has_w

        if count >= 4:
            prototype = protos.fourway
            facing = SOUTH
        elif count == 3:
            prototype = protos.t_junction
            facing = _t_junction_facing(has_n, has_s, has_e, has_w)
        elif count == 2:
            if (has_n and has_s) or (has_e and has_w):
                prototype = protos.straight
                facing = EAST if (has_e and has_w) else SOUTH
            else:
                prototype = protos.bend
                facing = _bend_facing(has_n, has_s, has_e, has_w)
        else:
            prototype = protos.straight
            facing = EAST if (has_e or has_w) else SOUTH

        results.append(FittedPipe(
            x=x, y=y, prototype=prototype, facing=facing,
            color=color if family == "gas" else None,
            pipe_layer=pipe_layer,
        ))

    return results


def _t_junction_facing(has_n: bool, has_s: bool, has_e: bool, has_w: bool) -> int:
    """SS14's T-junction default (facing SOUTH) = TSouth = connects S+E+W (missing N).
    Rotating which direction is missing transforms the facing the same way
    the reference editor's getTJunctionRotation does, just expressed as one
    of this converter's own 4 cardinal "facing" values instead of radians."""
    if not has_n:
        return SOUTH
    if not has_w:
        return EAST
    if not has_s:
        return NORTH
    if not has_e:
        return WEST
    return SOUTH  # unreachable with count == 3


def _bend_facing(has_n: bool, has_s: bool, has_e: bool, has_w: bool) -> int:
    """SS14's bend default (facing SOUTH) = SWBend = connects S+W."""
    if has_s and has_w:
        return SOUTH
    if has_e and has_s:
        return EAST
    if has_n and has_e:
        return NORTH
    if has_w and has_n:
        return WEST
    return SOUTH  # unreachable with count == 2 and not a straight
