"""Index of the prototype ids that a converted map is allowed to reference.

The converter has to answer two questions about every id a human types into the
mapping table: does it exist, and -- when it does not -- what did they probably
mean?  Loading the prototypes through YAML would be correct but slow (over
20 000 entity prototypes spread across thousands of files), and we only need the
ids, so the files are scanned line by line instead.
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from bisect import bisect_left

# Nearly every prototype file starts its list at column 0, but at least one
# (Entities/Structures/Furniture/sink.yml) indents the whole thing -- still
# valid YAML, since a top-level sequence only needs consistent indentation, not
# indentation of zero. Capturing the marker's own indent lets a field line be
# recognised relative to it instead of assuming column 0.
TYPE_LINE = re.compile(r"^(\s*)- type: (\S+)")
FIELD_LINE = re.compile(r"^(\s*)(\w+):\s*(.*)$")
# A bare (unquoted) trailing '# comment' on an id/abstract/variants line, e.g.
# 'id: PlushieLizard #Weh!' -- valid YAML, and common enough in this repo
# (242 occurrences) that leaving it in the captured value silently broke
# lookups for every id written that way. None of the three fields this
# scanner reads ever legitimately contains '#', so stripping from the first
# whitespace-preceded '#' is safe.
TRAILING_COMMENT = re.compile(r"\s+#.*$")

# Prototype kinds the converter can place on a map.
ENTITY = "entity"
TILE = "tile"
DECAL = "decal"


@dataclass
class ProtoIndex:
    entities: set[str] = field(default_factory=set)
    tiles: set[str] = field(default_factory=set)
    decals: set[str] = field(default_factory=set)
    tile_variants: dict[str, int] = field(default_factory=dict)
    abstract_entities: set[str] = field(default_factory=set)

    def kinds(self) -> dict[str, set[str]]:
        return {ENTITY: self.entities, TILE: self.tiles, DECAL: self.decals}

    def has(self, kind: str, proto_id: str) -> bool:
        return proto_id in self.kinds().get(kind, ())

    def variants(self, tile_id: str) -> int:
        """How many visual variants a tile prototype declares (at least one)."""
        return max(1, self.tile_variants.get(tile_id, 1))

    _prefix_cache: dict[str, list[tuple[str, str]]] = field(default_factory=dict, repr=False)

    def _prefix_pool(self, kind: str) -> list[tuple[str, str]]:
        """(lowercased, id) pairs of one kind, sorted so a prefix can be bisected."""
        if kind not in self._prefix_cache:
            self._prefix_cache[kind] = sorted(
                (candidate.lower(), candidate) for candidate in self.kinds().get(kind, ())
            )
        return self._prefix_cache[kind]

    def search(self, kind: str, text: str, limit: int = 12) -> list[str]:
        """Ids for a live autocomplete list: prefix matches first, then substring.

        This is deliberately looser than `suggest`. A suggestion is written into
        a table cell as an answer, so it may only be offered when it is likely
        right; a dropdown is a list someone reads and picks from, where showing
        every id containing "airlock" is exactly what is wanted.
        """
        if not text:
            return []
        lowered = text.lower()
        pool = self._prefix_pool(kind)

        prefixed = [candidate for low, candidate in pool if low.startswith(lowered)]
        prefixed.sort(key=lambda candidate: (len(candidate), candidate))
        if len(prefixed) >= limit:
            return prefixed[:limit]

        seen = set(prefixed)
        contained = sorted(
            (candidate for low, candidate in pool if lowered in low and candidate not in seen),
            key=lambda candidate: (len(candidate), candidate),
        )
        return (prefixed + contained)[:limit]

    def suggest(self, kind: str, wanted: str, limit: int = 1) -> list[str]:
        """Ids that plausibly match `wanted`, best first -- or nothing at all.

        Only exact and prefix matches are offered. Substring and fuzzy matching
        were tried and dropped: over 20 000 ids they answered "BlueprintFulton"
        for a burnt floor and "Firelock" for an airlock, and cost ~80ms a call,
        which was most of a scan's runtime. An empty suggestion tells a mapper
        to go and look; a confident wrong one invites them to accept it.
        """
        candidates = self.kinds().get(kind, set())
        if not candidates:
            return []
        if wanted in candidates:
            return [wanted]

        lowered = wanted.lower()
        pool = self._prefix_pool(kind)
        matches: list[str] = []
        for low, candidate in pool[bisect_left(pool, (lowered, "")):]:
            if not low.startswith(lowered):
                break
            matches.append(candidate)

        # Shortest first: the shortest id starting with "Airlock" is "Airlock".
        matches.sort(key=lambda candidate: (len(candidate), candidate))
        return matches[:limit]


def _flush(index: ProtoIndex, kind: str | None, fields: dict[str, str]) -> None:
    proto_id = fields.get("id")
    if kind is None or not proto_id:
        return

    if kind == ENTITY:
        if fields.get("abstract", "").lower() == "true":
            index.abstract_entities.add(proto_id)
        else:
            index.entities.add(proto_id)
    elif kind == TILE:
        index.tiles.add(proto_id)
        try:
            index.tile_variants[proto_id] = int(fields.get("variants", "1"))
        except ValueError:
            index.tile_variants[proto_id] = 1
    elif kind == DECAL:
        index.decals.add(proto_id)


def build(prototypes_dir: str) -> ProtoIndex:
    """Scan ``Resources/Prototypes`` for entity, tile and decal ids."""
    index = ProtoIndex()
    wanted = {ENTITY, TILE, DECAL}

    for root, _, files in os.walk(prototypes_dir):
        for name in files:
            if not name.endswith((".yml", ".yaml")):
                continue
            path = os.path.join(root, name)
            kind: str | None = None
            fields: dict[str, str] = {}
            field_indent = 0  # the indent a field line must have to belong to the current type
            try:
                with open(path, encoding="utf-8-sig") as handle:
                    for line in handle:
                        if (type_match := TYPE_LINE.match(line)) is not None:
                            _flush(index, kind, fields)
                            found = type_match.group(2)
                            kind = found if found in wanted else None
                            fields = {}
                            field_indent = len(type_match.group(1)) + 2
                        elif (
                            kind is not None
                            and (field_match := FIELD_LINE.match(line)) is not None
                            and len(field_match.group(1)) == field_indent
                        ):
                            key, value = field_match.group(2), field_match.group(3).strip()
                            if key in ("id", "abstract", "variants") and key not in fields:
                                fields[key] = TRAILING_COMMENT.sub("", value).strip()
            except (OSError, UnicodeDecodeError):
                continue
            _flush(index, kind, fields)

    return index
