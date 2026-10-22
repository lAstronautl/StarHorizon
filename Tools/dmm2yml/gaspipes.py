"""tg gas pipe atoms -> fitted SS14 gas pipe entities.

tg's /obj/machinery/atmospherics/pipe/smart/<shape>/<color>[/visible|hidden]
[/layerN] family (code/modules/atmospherics/machinery/pipes/{smart,mapping}.dm)
covers straight/bend/3-way/4-way with one DM type per shape, color baked into
the subtype name, and layer baked in as a path suffix. The shape a given atom
actually renders as is computed by the game from live node connections, not
authored per-instance, so its saved `dir` is engine output rather than intent
-- see pipefitting.py's docstring for why this module instead re-derives the
shape from real tile neighbors, the same way the reference editor's pipe-draw
tool does.

Two more tg pipe types share this exact colour/layer path scheme (same
mapping.dm HELPER() macro, see its `Fulltype` list) but sit directly under
.../pipe/ instead of .../pipe/smart/: bridge_pipe ("a one meter section of
regular pipe used to connect pipenets over pipes" -- lets one network cross
another's tile without merging) and layer_manifold ("a special pipe to
bridge pipe layers with" -- actively joins two different tg piping_layers
at one tile). Neither has an SS14 equivalent, and neither can: SharedAtmos-
PipeLayersComponent's own doc comment says the whole point of AtmosPipeLayer
is letting pipes on different layers "be anchored to the same tile without
their contents mixing" -- this engine has no code path for gas to cross
between layers or for two same-layer runs to stay deliberately unmerged
through one tile, so there is nothing a map (data only, no new game logic)
could point either path at to reproduce what they actually do. Converting
them as an ordinary same-colour/layer pipe segment -- letting them fold into
whatever straight/bend/junction their real neighbours call for, same as any
other tile of that colour/layer -- keeps the run's physical continuity
instead of leaving the gap ignore.yml left before, which is the best a
static conversion can do once the cross-layer/cross-network behaviour itself
is off the table. multiz (a cross-z-level connector) has no such fallback --
see ignore.yml's comment -- and heat_exchange is not a real tg pipe path at
all: a TEG's gas side is a plain /obj/machinery/atmospherics/components/
binary/circulator, already mapped in entities.yml like any other device.

Colour hex values: COLOR_BLUE/COLOR_RED match the reference editor's own
supply/scrubbers presets exactly (#0055CCFF / #990000FF); every other named
colour uses this tg checkout's own code/__DEFINES/colors.dm constant, alpha
FF appended to match AtmosPipeColor's #RRGGBBAA convention.
"""

from __future__ import annotations

from dataclasses import dataclass

PIPE_PREFIX = "/obj/machinery/atmospherics/pipe"

# code/modules/atmospherics/machinery/pipes/mapping.dm's HELPER() macro colours
# every one of these the same way, so they all parse with the same suffix
# rules below -- just two different prefixes under PIPE_PREFIX, per that
# macro's own Fulltype list (see the module docstring above).
_SMART_SHAPES = {"simple", "manifold", "manifold4w"}
_DIRECT_SHAPES = {"bridge_pipe", "layer_manifold"}
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
    shape: str  # "simple" | "manifold" | "manifold4w" | "bridge_pipe" | "layer_manifold"
    color_name: str  # a PIPE_COLOR_HEX key
    tg_layer: int  # 1-5


def parse_gas_pipe_path(path: str, allow_all_colors: bool = False) -> ParsedGasPipe | None:
    """Decode a .../pipe/smart/<shape>/<color>[.../layerN] or
    .../pipe/<bridge_pipe|layer_manifold>/<color>[.../layerN] path -- see the
    module docstring for why the latter two convert at all, and why multiz
    and anything else under PIPE_PREFIX does not (falls through to None,
    left for ignore.yml as before this function existed). Also None for any
    colour besides BASE_COLORS unless `allow_all_colors` -- the user's own
    instruction was "support every colour, but by default convert only the
    2 base ones".
    """
    if not (path == PIPE_PREFIX or path.startswith(PIPE_PREFIX + "/")):
        return None
    rest = path[len(PIPE_PREFIX):].strip("/")
    if not rest:
        return None
    parts = rest.split("/")

    if parts[0] == "smart" and len(parts) > 1 and parts[1] in _SMART_SHAPES:
        shape = parts[1]
        parts = parts[2:]
    elif parts[0] in _DIRECT_SHAPES:
        shape = parts[0]
        parts = parts[1:]
    else:
        return None

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
