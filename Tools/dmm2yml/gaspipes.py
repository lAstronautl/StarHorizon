"""tg "smart pipe" atoms -> fitted SS14 gas pipe entities.

tg's /obj/machinery/atmospherics/pipe/smart/<shape>/<color>[/visible|hidden]
[/layerN] family (code/modules/atmospherics/machinery/pipes/{smart,mapping}.dm)
covers straight/bend/3-way/4-way with one DM type per shape, color baked into
the subtype name, and layer baked in as a path suffix. The shape a given atom
actually renders as is computed by the game from live node connections, not
authored per-instance, so its saved `dir` is engine output rather than intent
-- see pipefitting.py's docstring for why this module instead re-derives the
shape from real tile neighbors, the same way the reference editor's pipe-draw
tool does.

Colour hex values: COLOR_BLUE/COLOR_RED match the reference editor's own
supply/scrubbers presets exactly (#0055CCFF / #990000FF); every other named
colour uses this tg checkout's own code/__DEFINES/colors.dm constant, alpha
FF appended to match AtmosPipeColor's #RRGGBBAA convention.
"""

from __future__ import annotations

from dataclasses import dataclass

GAS_PIPE_PREFIX = "/obj/machinery/atmospherics/pipe/smart"

_SHAPES = {"simple", "manifold", "manifold4w"}
_VISIBILITY = {"visible", "hidden"}

# code/modules/atmospherics/machinery/pipes/mapping.dm HELPER()/HELPER_NAMED() calls.
PIPE_COLOR_HEX: dict[str, str] = {
    "supply": "#0055CCFF",     # reference editor's PIPE_COLORS.supply (tg COLOR_BLUE "#0000FF")
    "scrubbers": "#990000FF",  # reference editor's PIPE_COLORS.return (tg COLOR_RED "#FF0000")
    "yellow": "#FFFF00FF",
    "cyan": "#00FFFFFF",
    "green": "#00FF00FF",      # COLOR_VIBRANT_LIME
    "orange": "#FFA62BFF",     # COLOR_ENGINEERING_ORANGE
    "purple": "#800080FF",
    "dark": "#454545FF",
    "brown": "#BA9F6DFF",
    "violet": "#6927C5FF",     # COLOR_STRONG_VIOLET
    "pink": "#FF3CC8FF",       # COLOR_LIGHT_PINK
    # "general" is ATMOS_COLOR_OMNI -- tg's "connects to any colour" sentinel.
    # No SS14 AtmosPipeColor component is the equivalent: GasPipeStraight and
    # friends carry none by default, which is what lets a plain pipe network
    # join any run in-game.
    "general": None,
}

# tg's piping_layer (1-5, see code/__DEFINES/atmospherics/atmos_piping.dm) vs.
# this fork's 3 simultaneous slots (AtmosPipeLayer Primary/Secondary/Tertiary,
# see pipefitting.py). Layers 1/2/3 take the matching slot number directly --
# there is nothing to decide about them. Layers 4 and 5 have no slot of their
# own; the caller is expected to ask the person converting the map which of
# slots 1/2/3 to fold each one onto (see dmm2yml.py's layer-overflow prompt),
# and to leave a layer out of the map entirely -- same as this default -- for
# "don't convert it".
DEFAULT_LAYER_MAP: dict[int, str] = {1: "Primary", 2: "Secondary", 3: "Tertiary"}
SS14_SLOT_NAMES: dict[int, str] = {1: "Primary", 2: "Secondary", 3: "Tertiary"}

# code/modules/atmospherics/machinery/pipes/mapping.dm's "base" colours -- the
# classic scrubbers (red) / supply (blue) pair every tg station pipes with by
# default. Every other named colour exists in PIPE_COLOR_HEX (for allow_all_colors)
# but is left unconverted unless explicitly asked for.
BASE_COLORS = {"scrubbers", "supply"}


@dataclass(frozen=True)
class ParsedGasPipe:
    shape: str  # "simple" | "manifold" | "manifold4w"
    color_name: str  # a PIPE_COLOR_HEX key
    tg_layer: int  # 1-5


def parse_gas_pipe_path(path: str, allow_all_colors: bool = False) -> ParsedGasPipe | None:
    """Decode a /smart/<shape>/<color>[/visible|hidden][/layerN] path.

    Returns None for anything under GAS_PIPE_PREFIX that isn't one of the
    three plain shapes (layer_manifold, multiz, bridge_pipe, heat_exchange/*
    all fall through here -- not yet handled by this converter), and for any
    colour besides BASE_COLORS unless `allow_all_colors` -- the user's own
    instruction was "support every colour, but by default convert only the
    2 base ones".
    """
    if not (path == GAS_PIPE_PREFIX or path.startswith(GAS_PIPE_PREFIX + "/")):
        return None
    rest = path[len(GAS_PIPE_PREFIX):].strip("/")
    if not rest:
        return None
    parts = rest.split("/")
    if parts[0] not in _SHAPES:
        return None
    shape = parts[0]
    parts = parts[1:]

    tg_layer = 3
    if parts and parts[-1].startswith("layer") and parts[-1][5:].isdigit():
        tg_layer = int(parts[-1][5:])
        parts = parts[:-1]
    if parts and parts[-1] in _VISIBILITY:
        parts = parts[:-1]

    color_name = parts[0] if parts else "general"
    if color_name not in PIPE_COLOR_HEX:
        return None
    if not allow_all_colors and color_name not in BASE_COLORS:
        return None
    return ParsedGasPipe(shape=shape, color_name=color_name, tg_layer=tg_layer)
