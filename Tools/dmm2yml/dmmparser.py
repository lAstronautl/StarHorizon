"""Parser for BYOND ``.dmm`` map files, both the standard and the TGM dialect.

A ``.dmm`` file has two parts.  First a dictionary that maps a short key to the
contents of a tile::

    "aah" = (
    /obj/effect/decal/cleanable/blood/old,
    /obj/machinery/camera/directional/east{
    	c_tag = "Science Maintenance Corridor";
    	network = list("ss13","rd")
    	},
    /turf/open/floor/iron/white,
    /area/station/science/research)

Then a grid that places those keys.  Standard ``.dmm`` writes one block holding
whole rows; TGM (what ``dmm2tgm.py`` emits) writes one block per column::

    (1,1,1) = {"
    aaa
    aag
    "}

Both are handled by the same code: a block is a rectangle whose top-left corner
is derived from the block header, and rows are listed from the highest y
downwards -- which is how BYOND itself writes maps.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any

BLOCK_HEADER = re.compile(r'^\((-?\d+),(-?\d+),(-?\d+)\) = \{"$', re.M)


@dataclass
class Atom:
    """A single ``/obj/...``, ``/turf/...``, ``/area/...`` entry on a tile."""

    path: str
    vars: dict[str, Any] = field(default_factory=dict)

    @property
    def kind(self) -> str:
        """``turf``, ``area``, ``decal``, ``mob`` or ``entity``."""
        if self.path.startswith("/turf"):
            return "turf"
        if self.path.startswith("/area"):
            return "area"
        if self.path.startswith(("/obj/effect/turf_decal", "/obj/effect/decal")):
            return "decal"
        if self.path.startswith("/mob"):
            return "mob"
        return "entity"


@dataclass
class DmmMap:
    definitions: dict[str, list[Atom]]
    grid: dict[tuple[int, int, int], str]
    width: int
    height: int
    z_levels: list[int]
    key_length: int

    def atoms_at(self, x: int, y: int, z: int) -> list[Atom]:
        key = self.grid.get((x, y, z))
        return self.definitions.get(key, []) if key else []


class DmmParseError(Exception):
    pass


def _split_top_level(text: str, separator: str) -> list[str]:
    """Split on ``separator``, ignoring anything nested or inside a string."""
    parts: list[str] = []
    current: list[str] = []
    depth = 0
    in_string = False
    escaped = False

    for char in text:
        if in_string:
            current.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue

        if char == '"':
            in_string = True
            current.append(char)
        elif char in "({[":
            depth += 1
            current.append(char)
        elif char in ")}]":
            depth -= 1
            current.append(char)
        elif char == separator and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(char)

    parts.append("".join(current))
    return [part.strip() for part in parts if part.strip()]


def _parse_value(raw: str) -> Any:
    raw = raw.strip()
    if raw.startswith('"') and raw.endswith('"') and len(raw) >= 2:
        return raw[1:-1].replace('\\"', '"')
    if raw.startswith("list(") and raw.endswith(")"):
        return [_parse_value(item) for item in _split_top_level(raw[5:-1], ",")]
    lowered = raw.lower()
    if lowered in ("null", "none"):
        return None
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    try:
        return int(raw)
    except ValueError:
        pass
    try:
        return float(raw)
    except ValueError:
        pass
    return raw


def _parse_atom(raw: str) -> Atom:
    brace = raw.find("{")
    if brace == -1:
        return Atom(path=raw.strip())

    path = raw[:brace].strip()
    body = raw[brace + 1 : raw.rindex("}")]
    variables: dict[str, Any] = {}
    for assignment in _split_top_level(body, ";"):
        name, _, value = assignment.partition("=")
        if not _:
            continue
        variables[name.strip()] = _parse_value(value)
    return Atom(path=path, vars=variables)


def _parse_definitions(header: str) -> tuple[dict[str, list[Atom]], int]:
    definitions: dict[str, list[Atom]] = {}
    key_lengths: set[int] = set()

    position = 0
    pattern = re.compile(r'^"([^"\n]+)" = \(', re.M)
    while (match := pattern.search(header, position)) is not None:
        key = match.group(1)
        key_lengths.add(len(key))

        # Walk forward to the parenthesis that closes this definition.
        depth = 0
        index = match.end() - 1
        in_string = False
        escaped = False
        while index < len(header):
            char = header[index]
            if in_string:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    in_string = False
            elif char == '"':
                in_string = True
            elif char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
                if depth == 0:
                    break
            index += 1
        else:
            raise DmmParseError(f'unterminated definition for key "{key}"')

        body = header[match.end() : index]
        definitions[key] = [_parse_atom(part) for part in _split_top_level(body, ",")]
        position = index + 1

    if not definitions:
        raise DmmParseError("no tile definitions found; is this really a .dmm file?")
    if len(key_lengths) != 1:
        raise DmmParseError(f"inconsistent key lengths in dictionary: {sorted(key_lengths)}")

    return definitions, key_lengths.pop()


def parse(path: str) -> DmmMap:
    with open(path, encoding="utf-8") as handle:
        text = handle.read()

    first_block = BLOCK_HEADER.search(text)
    if first_block is None:
        raise DmmParseError("no map blocks found; is this really a .dmm file?")

    definitions, key_length = _parse_definitions(text[: first_block.start()])

    grid: dict[tuple[int, int, int], str] = {}
    body = text[first_block.start() :]
    headers = list(BLOCK_HEADER.finditer(body))
    for number, header in enumerate(headers):
        start = header.end() + 1
        end = body.find('\n"}', start)
        if end == -1:
            raise DmmParseError(f"unterminated map block at {header.group(0)}")
        rows = body[start:end].split("\n")

        x0, y0, z = (int(header.group(index)) for index in (1, 2, 3))
        for row_index, row in enumerate(rows):
            # Rows run from the top of the block downwards.
            y = y0 + (len(rows) - 1 - row_index)
            if len(row) % key_length:
                raise DmmParseError(
                    f"row {row_index} of block {header.group(0)} is not a multiple "
                    f"of the {key_length}-character key length"
                )
            for column in range(len(row) // key_length):
                key = row[column * key_length : (column + 1) * key_length]
                if key not in definitions:
                    raise DmmParseError(f'row {row_index} of block {header.group(0)} uses unknown key "{key}"')
                grid[(x0 + column, y, z)] = key

        if number == len(headers) - 1:
            break

    xs = [x for x, _, _ in grid]
    ys = [y for _, y, _ in grid]
    zs = sorted({z for _, _, z in grid})
    return DmmMap(
        definitions=definitions,
        grid=grid,
        width=max(xs) - min(xs) + 1,
        height=max(ys) - min(ys) + 1,
        z_levels=zs,
        key_length=key_length,
    )
