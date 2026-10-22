"""Which cardinal directions a placed entity's atmos pipe-network ports face.

Ported from the reference editor's src/algorithms/pipeDirections.ts, adapted to
read this fork's actual prototype YAML (Resources/Prototypes/Entities/Structures/
Piping/Atmospherics/) instead of a live in-browser registry. Used by the pipe
auto-fit so a drawn run bends toward a vent/pump/scrubber's real open port
instead of only ever recognising another pipe segment.

Scoped to just the Piping/Atmospherics folder rather than all ~20 000 entity
prototypes: every device with a pipe-network NodeContainer lives there, and
a full YAML parse of a few dozen files is cheap where doing it fork-wide
would not be (see protoindex.py's own docstring on why that one stays a line
scanner).
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from typing import Any

import yaml


class _LenientLoader(yaml.SafeLoader):
    """SafeLoader, but an unknown `!type:X` tag (e.g. !type:PipeNode on a
    NodeContainer node) parses as a plain mapping/sequence/scalar instead of
    raising -- these files use that tag throughout and safe_load alone drops
    the whole file on the first one."""


def _construct_typed(loader: yaml.SafeLoader, tag_suffix: str, node: yaml.Node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


_LenientLoader.add_multi_constructor("!type:", _construct_typed)

NORTH, SOUTH, EAST, WEST = 1, 2, 4, 8
DIRECTIONS = (NORTH, SOUTH, EAST, WEST)

# Named pipeDirection flag combos used by SS14 pipe-node prototypes.
PIPE_DIRECTION_FLAGS: dict[str, int] = {
    "North": NORTH, "South": SOUTH, "East": EAST, "West": WEST,
    "Longitudinal": NORTH | SOUTH,
    "Lateral": EAST | WEST,
    "NEBend": NORTH | EAST, "NWBend": NORTH | WEST,
    "SEBend": SOUTH | EAST, "SWBend": SOUTH | WEST,
    "TNorth": EAST | WEST | NORTH, "TSouth": EAST | WEST | SOUTH,
    "TEast": NORTH | SOUTH | EAST, "TWest": NORTH | SOUTH | WEST,
    "Fourway": NORTH | SOUTH | EAST | WEST,
}


def _bits_for(raw: str) -> int:
    bits = 0
    for part in raw.replace("|", ",").split(","):
        part = part.strip()
        if not part:
            continue
        if part in PIPE_DIRECTION_FLAGS:
            bits |= PIPE_DIRECTION_FLAGS[part]
        elif part in ("North", "South", "East", "West"):
            bits |= PIPE_DIRECTION_FLAGS[part]
    return bits


@dataclass
class PipeNodeIndex:
    _parents: dict[str, list[str]] = field(default_factory=dict)
    # id -> bitmask of this id's OWN NodeContainer's Pipe-group node directions
    # (South-baseline, i.e. before any placement rotation is applied), or None
    # if this id has no NodeContainer component of its own at all (falls
    # through to its parents) vs 0 if it has one with no Pipe-group nodes
    # (a real dead end -- stop looking, don't fall through).
    _own_bits: dict[str, int | None] = field(default_factory=dict)

    def directions(self, proto_id: str) -> int:
        """South-baseline pipe-port bitmask for `proto_id` (0 if it has none)."""
        seen: set[str] = set()
        pid: str | None = proto_id
        while pid is not None and pid not in seen:
            seen.add(pid)
            bits = self._own_bits.get(pid)
            if bits is not None:
                return bits
            parents = self._parents.get(pid)
            if not parents:
                return 0
            # SS14 lets a child list more than one parent (later overrides
            # earlier); real atmos devices never actually split NodeContainer
            # across siblings, so checking each in turn and taking the first
            # hit is equivalent in practice and far simpler than a full merge.
            for parent in parents:
                if parent not in seen:
                    found = self.directions(parent)
                    if found:
                        return found
            return 0
        return 0


def build(prototypes_dir: str) -> PipeNodeIndex:
    index = PipeNodeIndex()
    piping_dir = os.path.join(prototypes_dir, "Entities", "Structures", "Piping", "Atmospherics")
    if not os.path.isdir(piping_dir):
        return index

    for root, _, files in os.walk(piping_dir):
        for name in files:
            if not name.endswith((".yml", ".yaml")):
                continue
            path = os.path.join(root, name)
            try:
                with open(path, encoding="utf-8-sig") as handle:
                    data = yaml.load(handle, Loader=_LenientLoader)
            except (OSError, yaml.YAMLError, UnicodeDecodeError):
                continue
            if not isinstance(data, list):
                continue
            for entry in data:
                if not isinstance(entry, dict) or entry.get("type") != "entity":
                    continue
                proto_id = entry.get("id")
                if not proto_id:
                    continue
                parent = entry.get("parent")
                if isinstance(parent, str):
                    index._parents[proto_id] = [parent]
                elif isinstance(parent, list):
                    index._parents[proto_id] = [str(p) for p in parent]

                nc = _find_component(entry.get("components"), "NodeContainer")
                if nc is None:
                    continue
                nodes = nc.get("nodes")
                bits = 0
                if isinstance(nodes, dict):
                    for node in nodes.values():
                        if not isinstance(node, dict):
                            continue
                        if node.get("nodeGroupID") != "Pipe":
                            continue
                        raw = node.get("pipeDirection")
                        if isinstance(raw, str):
                            bits |= _bits_for(raw)
                index._own_bits[proto_id] = bits

    return index


def _find_component(components: Any, type_name: str) -> dict[str, Any] | None:
    if not isinstance(components, list):
        return None
    found: dict[str, Any] | None = None
    for comp in components:
        if isinstance(comp, dict) and comp.get("type") == type_name:
            found = comp  # last one wins, matching a single components list's own override order
    return found
