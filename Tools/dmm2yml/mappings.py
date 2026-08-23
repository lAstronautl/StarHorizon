"""The substitution tables that turn SS13 paths into SS14 content.

SS13 paths are a type hierarchy, so a lookup falls back to the nearest parent
path that has a rule: a rule for ``/turf/open/floor/iron/dark`` also covers
``/turf/open/floor/iron/dark/textured_corner``.  That is what makes it possible
to cover a 2700-path map with a few hundred rules.  Which lookups were exact and
which were inherited is reported by the scan, so nothing is silently guessed.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from typing import Any

import yaml

SKIP = "skip"  # what a human writes to say "drop this on purpose"
NORTH, SOUTH, EAST, WEST = 1, 2, 4, 8
DIRECTIONS = (NORTH, SOUTH, EAST, WEST)

# "Mounted on the north wall" and "facing north" are opposites, and SS13 and
# SS14 disagree about which one they record. SS13 writes the wall
# ("/obj/machinery/airalarm/directional/north" sits against a wall to its
# north); SS14 writes the facing, so that alarm looks south, away from it.
# A rule says which it means, and this table converts one to the other.
WALL_TO_FACING = {"north": SOUTH, "south": NORTH, "east": WEST, "west": EAST}


class MappingError(Exception):
    pass


@dataclass
class TurfRule:
    tile: str | None = None
    entity: str | None = None


@dataclass
class DecalRule:
    # Either a single id, one id per BYOND dir, or several ids on one tile.
    decal_id: str | None = None
    dirs: dict[int, str] = field(default_factory=dict)
    ids: list[str] = field(default_factory=list)
    color: str = "#FFFFFFFF"
    angle: float | None = None
    z_index: int | None = None
    cleanable: bool | None = None

    def ids_for(self, direction: int) -> list[str]:
        if self.dirs:
            return [self.dirs[direction]] if direction in self.dirs else []
        if self.ids:
            return list(self.ids)
        return [self.decal_id] if self.decal_id else []


@dataclass
class EntityRule:
    entities: list[str] = field(default_factory=list)
    # The facing to use, when the path implies one that no dir var records.
    # Set from either "dir" (the way it looks) or "wall" (the side it is
    # mounted on, which is the opposite).
    direction: int | None = None
    # Some SS13 objects are tiles in SS14: /obj/structure/lattice is an object
    # there and the "Lattice" tile here.
    tile: str | None = None
    # SS13 places a wall-mounted object on the room's floor tile, next to the
    # wall. SS14 does not: an AirAlarm, APC, FireAlarm, intercom, extinguisher
    # cabinet or door signal button sits ON the wall's own tile, embedded in it
    # -- confirmed against Resources/Maps/amber.yml, where 100% of these sit on
    # the same tile as a WallSolid, never the neighbouring floor tile. A light or
    # camera, by contrast, stays on the floor (0% on a wall tile there). Setting
    # this moves the converted entity one tile toward the wall it names.
    on_wall: bool = False


@dataclass
class Resolution:
    """The outcome of looking one DMM path up in the tables."""

    rule: Any | None
    matched_path: str | None
    exact: bool
    skipped: bool = False


def _as_bool(value: Any) -> bool | None:
    return None if value is None else bool(value)


def _parse_turf(path: str, raw: Any) -> TurfRule:
    if raw is None or (isinstance(raw, str) and raw.strip().lower() == SKIP):
        return TurfRule()
    if isinstance(raw, str):
        return TurfRule(tile=raw)
    if isinstance(raw, dict):
        return TurfRule(tile=raw.get("tile"), entity=raw.get("entity"))
    raise MappingError(f"{path}: a turf rule must be a tile id or a mapping, got {type(raw).__name__}")


def _parse_decal(path: str, raw: Any) -> DecalRule:
    if isinstance(raw, str):
        return DecalRule(decal_id=raw)
    if not isinstance(raw, dict):
        raise MappingError(f"{path}: a decal rule must be an id or a mapping, got {type(raw).__name__}")

    dirs = {int(key): str(value) for key, value in (raw.get("dirs") or {}).items()}
    for direction in dirs:
        if direction not in DIRECTIONS:
            raise MappingError(f"{path}: dir {direction} is not one of {DIRECTIONS}")

    return DecalRule(
        decal_id=raw.get("id"),
        dirs=dirs,
        ids=list(raw.get("decals") or []),
        color=raw.get("color", "#FFFFFFFF"),
        angle=raw.get("angle"),
        z_index=raw.get("zIndex"),
        cleanable=_as_bool(raw.get("cleanable")),
    )


def _parse_entity(path: str, raw: Any) -> EntityRule:
    if raw is None or (isinstance(raw, str) and raw.strip().lower() == SKIP):
        return EntityRule()
    if isinstance(raw, str):
        return EntityRule(entities=[raw])
    if isinstance(raw, list):
        return EntityRule(entities=[str(item) for item in raw])
    if isinstance(raw, dict):
        if "dir" in raw and "wall" in raw:
            raise MappingError(f"{path}: set either dir or wall, not both")

        direction = raw.get("dir")
        if direction is not None:
            direction = int(direction)
            if direction not in DIRECTIONS:
                raise MappingError(f"{path}: dir {direction} is not one of {DIRECTIONS}")
        elif (wall := raw.get("wall")) is not None:
            if wall not in WALL_TO_FACING:
                raise MappingError(f"{path}: wall must be one of {sorted(WALL_TO_FACING)}, got '{wall}'")
            direction = WALL_TO_FACING[wall]
        tile = raw.get("tile")
        on_wall = bool(raw.get("onWall", False))
        if on_wall and "wall" not in raw:
            raise MappingError(f"{path}: onWall needs a wall side, e.g. {{wall: north, onWall: true}}")
        if "entities" in raw:
            return EntityRule(
                entities=[str(i) for i in raw["entities"]], direction=direction, tile=tile, on_wall=on_wall
            )
        return EntityRule(
            entities=[str(raw["entity"])] if raw.get("entity") else [],
            direction=direction,
            tile=tile,
            on_wall=on_wall,
        )
    raise MappingError(f"{path}: an entity rule must be a prototype id or a list, got {type(raw).__name__}")


def _load_yaml(path: str) -> dict[str, Any]:
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as handle:
        data = yaml.safe_load(handle)
    if data is None:
        return {}
    if not isinstance(data, dict):
        raise MappingError(f"{path}: expected a mapping at the top level")
    return data


def _lookup(table: dict[str, Any], path: str) -> tuple[Any | None, str | None, bool]:
    """Exact hit, else the longest parent path that has a rule.

    Walks up the path one segment at a time instead of scanning the table, so a
    lookup costs a handful of dict hits rather than a pass over every rule.
    """
    if path in table:
        return table[path], path, True

    parts = path.split("/")
    for cut in range(len(parts) - 1, 1, -1):
        candidate = "/".join(parts[:cut])
        if candidate in table:
            return table[candidate], candidate, False
    return None, None, False


@dataclass
class MappingSet:
    turfs: dict[str, TurfRule] = field(default_factory=dict)
    decals: dict[str, DecalRule] = field(default_factory=dict)
    entities: dict[str, EntityRule] = field(default_factory=dict)
    ignore: list[str] = field(default_factory=list)

    def table_for(self, kind: str) -> dict[str, Any]:
        if kind == "turf":
            return self.turfs
        if kind == "decal":
            return self.decals
        return self.entities  # entity, mob and area all resolve against the same table

    def is_ignored(self, path: str) -> bool:
        return any(path == prefix or path.startswith(prefix + "/") for prefix in self.ignore)

    def resolve(self, path: str, kind: str) -> Resolution:
        if self.is_ignored(path):
            return Resolution(rule=None, matched_path=None, exact=True, skipped=True)

        rule, matched, exact = _lookup(self.table_for(kind), path)
        if rule is None and kind == "decal":
            # Not everything SS13 calls a decal has an SS14 decal to become --
            # cobwebs, for one, are entities there. Fall back to that table so a
            # rule in entities.yml still works.
            rule, matched, exact = _lookup(self.entities, path)
        if rule is None:
            return Resolution(rule=None, matched_path=None, exact=False)
        return Resolution(rule=rule, matched_path=matched, exact=exact)


def load(mapping_dir: str) -> MappingSet:
    mappings = MappingSet()

    for path, raw in _load_yaml(os.path.join(mapping_dir, "turfs.yml")).items():
        mappings.turfs[path] = _parse_turf(path, raw)
    for path, raw in _load_yaml(os.path.join(mapping_dir, "decals.yml")).items():
        mappings.decals[path] = _parse_decal(path, raw)
    for path, raw in _load_yaml(os.path.join(mapping_dir, "entities.yml")).items():
        mappings.entities[path] = _parse_entity(path, raw)

    ignore_file = os.path.join(mapping_dir, "ignore.yml")
    if os.path.exists(ignore_file):
        with open(ignore_file, encoding="utf-8") as handle:
            data = yaml.safe_load(handle) or []
        if not isinstance(data, list):
            raise MappingError(f"{ignore_file}: expected a list of path prefixes")
        mappings.ignore = [str(item) for item in data]

    return mappings
