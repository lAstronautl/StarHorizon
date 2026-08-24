"""Checks that the converter still does what it was built to do.

Every check here exists because the thing it checks was got wrong at least
once. They run against a tiny map written out below, whose expected output is
worked out by hand, plus the real maps and prototypes of this repo -- so a
prototype renamed upstream, or a chunk format changed by an engine bump, shows
up here rather than in a map nobody can load.
"""

from __future__ import annotations

import base64
import math
import os
import re
import tempfile
from dataclasses import dataclass

import yaml

import dmmparser
import mappings as mapping_rules
import protoindex
import ss14map

# A map two tiles wide and four tall, in TGM layout: one block per column,
# rows listed from the top down. Written out in full so the expectations below
# can be checked by eye.
#
#          x=1              x=2                       x=3                x=4
#   y=4    space            hazard stripe facing east, --                 --
#                                 on dark floor
#   y=3    steel floor      catwalk over space          --                --
#   y=2    wall             space                       --                --
#   y=1    steel floor,     space                       steel floor,      steel floor,
#          light on the                                 air alarm mounted disposal pipe bent
#          east wall                                    on the east wall  SOUTHEAST (dir 6)
FIXTURE = '''"aa" = (
/turf/open/space/basic,
/area/space)
"ab" = (
/turf/open/floor/iron,
/area/station/hallway/primary/central)
"ac" = (
/turf/closed/wall,
/area/station/hallway/primary/central)
"ad" = (
/obj/machinery/light/small/directional/east,
/turf/open/floor/iron,
/area/station/hallway/primary/central)
"ae" = (
/obj/effect/turf_decal/stripes/line{
\tdir = 4
\t},
/turf/open/floor/iron/dark/textured,
/area/station/hallway/primary/central)
"af" = (
/obj/structure/lattice/catwalk,
/turf/open/space/basic,
/area/space)
"ag" = (
/obj/machinery/airalarm/directional/east,
/turf/open/floor/iron,
/area/station/hallway/primary/central)
"ah" = (
/obj/structure/disposalpipe/segment{
\tdir = 6
\t},
/turf/open/floor/iron,
/area/station/hallway/primary/central)

(1,1,1) = {"
aa
ab
ac
ad
"}
(2,1,1) = {"
ae
af
aa
aa
"}
(3,1,1) = {"
ag
"}
(4,1,1) = {"
ah
"}
'''

# What that map has to come out as. Tile coordinates are zero-based, so the
# .dmm cell (1,1) is tile (0,0).
EXPECTED_TILES = {
    (0, 0): "FloorSteel",
    (0, 1): "Plating",       # the wall turf leaves plating under the wall
    (0, 2): "FloorSteel",
    (1, 2): "Lattice",       # a catwalk is an object in SS13 and a tile here
    (1, 3): "FloorDark",     # /iron/dark/textured inherits the /iron/dark rule
}
EXPECTED_ENTITIES = {
    ("WallSolid", 0.5, 1.5): None,
    # Mounted on the wall to its east, so it looks west: -pi/2. A light stays on
    # the room's floor tile -- SS13's own placement.
    ("PoweredSmallLight", 0.5, 0.5): -1.5707963267948966,
    ("Catwalk", 1.5, 2.5): None,
    # An air alarm on the same east wall instead sits ON the wall's tile in
    # SS14 (tile (2,0) -> (3,0), one east of where the .dmm placed it): see
    # EntityRule.on_wall. Confirmed against Resources/Maps/amber.yml, where
    # every AirAlarm/APC/FireAlarm/etc. shares its tile with a wall.
    ("AirAlarm", 3.5, 0.5): -1.5707963267948966,
    # SOUTHEAST (dir 6 = SOUTH|EAST) is BYOND's diagonal-dir trick for "a bend
    # connecting south and east", not a facing -- see EntityRule.by_dir and
    # the comment above /obj/structure/disposalpipe/segment in entities.yml.
    ("DisposalBend", 3.5, 0.5): 1.5707963267948966,
}
EXPECTED_DECAL = ("WarnLineE", 1, 3)

MAX_MAPS_SAMPLED = 60


def dmm2yml_module():
    """Imported lazily: dmm2yml imports this module for its `selftest` command."""
    import dmm2yml

    return dmm2yml


@dataclass
class Result:
    name: str
    passed: bool
    detail: str


def _build_fixture(mapping_set, index) -> tuple[ss14map.MapBuilder, object]:
    import dmm2yml

    with tempfile.TemporaryDirectory() as directory:
        path = os.path.join(directory, "fixture.dmm")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(FIXTURE)
        dmm = dmmparser.parse(path)

    template = yaml.safe_load(
        open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "mapping", "grid_template.yml"),
             encoding="utf-8")
    )
    builder = ss14map.MapBuilder(template["map_entity"], template["grid_entity"], "0.0.0")
    survey = dmm2yml.walk(dmm, mapping_set, index, 1, builder, "deterministic")
    return builder, survey


def check_parser() -> Result:
    """The .dmm grid must come out the right way up and the right way round."""
    with tempfile.TemporaryDirectory() as directory:
        path = os.path.join(directory, "fixture.dmm")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(FIXTURE)
        dmm = dmmparser.parse(path)

    problems = []
    if (dmm.width, dmm.height) != (4, 4):
        problems.append(f"size {dmm.width}x{dmm.height}, expected 4x4")

    # Rows are listed top down, so the first row of a block is the highest y.
    if dmm.grid.get((1, 4, 1)) != "aa":
        problems.append("cell (1,4) is not the first row of column 1 -- y axis is flipped")
    if dmm.grid.get((1, 1, 1)) != "ad":
        problems.append("cell (1,1) is not the last row of column 1")
    if dmm.grid.get((2, 3, 1)) != "af":
        problems.append("cell (2,3) wrong -- columns and rows are transposed")

    decal = next(a for a in dmm.definitions["ae"] if a.kind == "decal")
    if decal.vars.get("dir") != 4:
        problems.append(f"dir var parsed as {decal.vars.get('dir')!r}, expected 4")

    bend = next(a for a in dmm.definitions["ah"] if a.kind == "entity")
    if bend.vars.get("dir") != 6:
        problems.append(f"disposal segment dir parsed as {bend.vars.get('dir')!r}, expected 6")

    return Result("parser: geometry and vars", not problems, "; ".join(problems) or "4x4, top-down rows, dir parsed")


def check_rules(mapping_set) -> Result:
    """Exact rules, inherited rules and ignore rules must behave as documented."""
    problems = []

    exact = mapping_set.resolve("/turf/open/floor/iron", "turf")
    if not exact.exact or exact.rule.tile != "FloorSteel":
        problems.append("exact turf lookup failed")

    inherited = mapping_set.resolve("/turf/open/floor/iron/dark/textured", "turf")
    if inherited.rule is None:
        problems.append("inheritance found nothing for /turf/open/floor/iron/dark/textured")
    else:
        if inherited.exact or inherited.matched_path != "/turf/open/floor/iron/dark":
            problems.append(
                f"inheritance picked {inherited.matched_path!r}, expected /turf/open/floor/iron/dark"
            )
        # The longer parent must win over the shorter one.
        if inherited.rule.tile != "FloorDark":
            problems.append("inheritance chose the shorter parent rule")

    if not mapping_set.resolve("/area/station/hallway/primary/central", "entity").skipped:
        problems.append("/area is not being ignored")

    if mapping_set.resolve("/obj/nothing/here/at/all", "entity").rule is not None:
        problems.append("an unknown path resolved to something")

    return Result("rules: exact, inherited, ignored", not problems, "; ".join(problems) or "all four behave")


def check_orientation(mapping_set) -> Result:
    """`wall` and `dir` must not be confused -- they are 180 degrees apart."""
    problems = []

    mounted = mapping_set.resolve("/obj/machinery/light/small/directional/east", "entity").rule
    if mounted is None:
        problems.append("no rule for a wall-mounted light")
    elif mounted.direction != mapping_rules.WEST:
        problems.append(f"wall:east gave facing {mounted.direction}, expected {mapping_rules.WEST} (west)")

    facing = mapping_set.resolve("/obj/structure/chair/stool/directional/north", "entity").rule
    if facing is None:
        problems.append("no rule for a directional stool")
    elif facing.direction != mapping_rules.NORTH:
        problems.append(f"dir:1 gave facing {facing.direction}, expected {mapping_rules.NORTH}")

    # Verified against the wall-mounted machines of Resources/Maps/amber.yml.
    expected = {
        mapping_rules.SOUTH: None,
        mapping_rules.EAST: 1.5707963267948966,
        mapping_rules.NORTH: 3.141592653589793,
        mapping_rules.WEST: -1.5707963267948966,
    }
    for direction, rotation in expected.items():
        if ss14map.rotation_for_dir(direction) != rotation:
            problems.append(f"dir {direction} -> {ss14map.rotation_for_dir(direction)}, expected {rotation}")

    return Result("orientation: wall vs dir", not problems, "; ".join(problems) or "wall inverts, dir does not")


def check_dictionaries(mapping_set, index) -> Result:
    """Every id the shipped mapping files name must still exist."""
    problems = []

    for path, rule in mapping_set.turfs.items():
        if rule.tile and not index.has(protoindex.TILE, rule.tile):
            problems.append(f"{path}: no tile {rule.tile}")
        if rule.entity and not index.has(protoindex.ENTITY, rule.entity):
            problems.append(f"{path}: no entity {rule.entity}")

    for path, rule in mapping_set.decals.items():
        ids = set(rule.dirs.values()) | set(rule.ids)
        if rule.decal_id:
            ids.add(rule.decal_id)
        for decal_id in sorted(ids):
            if not index.has(protoindex.DECAL, decal_id):
                problems.append(f"{path}: no decal {decal_id}")

    for path, rule in mapping_set.entities.items():
        if rule.tile and not index.has(protoindex.TILE, rule.tile):
            problems.append(f"{path}: no tile {rule.tile}")
        for entity in rule.entities:
            if not index.has(protoindex.ENTITY, entity):
                problems.append(f"{path}: no entity {entity}")
        for dmm_dir, variant in rule.by_dir.items():
            for entity in variant.entities:
                if not index.has(protoindex.ENTITY, entity):
                    problems.append(f"{path}: byDir[{dmm_dir}]: no entity {entity}")

    counted = len(mapping_set.turfs) + len(mapping_set.decals) + len(mapping_set.entities)
    detail = "; ".join(problems[:5]) if problems else f"{counted} rules, every id exists"
    if len(problems) > 5:
        detail += f" (and {len(problems) - 5} more)"
    return Result("dictionaries: ids resolve", not problems, detail)


def check_conversion(mapping_set, index) -> Result:
    """The fixture must convert to exactly the map worked out by hand."""
    builder, survey = _build_fixture(mapping_set, index)
    problems = []

    tiles = {position: tile_id for position, (tile_id, _, _) in builder.tiles.items()}
    for position, expected in EXPECTED_TILES.items():
        if tiles.get(position) != expected:
            problems.append(f"tile {position}: {tiles.get(position)!r}, expected {expected!r}")

    placed = {(e.proto, e.x, e.y): e.rotation for e in builder.entities}
    for key, rotation in EXPECTED_ENTITIES.items():
        if key not in placed:
            problems.append(f"missing entity {key[0]} at {key[1]},{key[2]}")
        elif placed[key] != rotation:
            problems.append(f"{key[0]}: rot {placed[key]}, expected {rotation}")

    decal_id, decal_x, decal_y = EXPECTED_DECAL
    found = any(
        node.decal_id == decal_id and (decal_x, decal_y) in positions
        for node, positions in builder.decals.items()
    )
    if not found:
        problems.append(f"decal {decal_id} missing at {decal_x},{decal_y}")

    if survey.unresolved:
        problems.append(f"unexpected unresolved paths: {sorted(survey.unresolved)}")

    return Result("conversion: fixture map", not problems, "; ".join(problems) or "tiles, entities, rotation, decal")


def check_rendering(mapping_set, index) -> Result:
    """The rendered document must be loadable YAML with the right shape."""
    builder, _ = _build_fixture(mapping_set, index)
    text = builder.render()
    problems = []

    class Loader(yaml.SafeLoader):
        pass

    # SS14 uses !type: tags that plain YAML does not know; they are not ours to
    # interpret, only to preserve.
    Loader.add_multi_constructor("!type:", lambda loader, suffix, node: None)

    try:
        document = yaml.load(text, Loader=Loader)
    except yaml.YAMLError as error:
        return Result("rendering: valid map document", False, f"output is not valid YAML: {error}")

    if document["meta"]["format"] != ss14map.MAP_FORMAT:
        problems.append(f"format {document['meta']['format']}, expected {ss14map.MAP_FORMAT}")
    if document["tilemap"].get(0) != ss14map.SPACE_TILE:
        problems.append("tile id 0 is not Space")
    if document["maps"] != [ss14map.MAP_ENTITY_UID] or document["grids"] != [ss14map.GRID_ENTITY_UID]:
        problems.append("maps/grids do not point at the map and grid entities")

    grid = document["entities"][0]["entities"][1]["components"]
    types = [component["type"] for component in grid]
    for required in ("MapGrid", "DecalGrid", "Transform", "MetaData"):
        if required not in types:
            problems.append(f"grid entity has no {required}")

    uids = [
        entity["uid"]
        for group in document["entities"]
        for entity in group["entities"]
    ]
    if len(uids) != len(set(uids)):
        problems.append("duplicate entity uids")

    return Result("rendering: valid map document", not problems, "; ".join(problems) or f"{len(uids)} entities, uids unique")


def check_chunk_roundtrip(repo_root: str) -> Result:
    """Re-encoding a chunk from a real map must reproduce it byte for byte."""
    maps_dir = os.path.join(repo_root, "Resources", "Maps")
    checked = failed = 0
    sampled = 0

    for root, _, files in os.walk(maps_dir):
        for name in sorted(files):
            if not name.endswith(".yml") or sampled >= MAX_MAPS_SAMPLED:
                continue
            with open(os.path.join(root, name), encoding="utf-8") as handle:
                text = handle.read()
            if "version: 7" not in text:
                continue  # older chunks use a shorter record
            sampled += 1
            for encoded in re.findall(r"tiles: ([A-Za-z0-9+/=]+)\n", text)[:50]:
                try:
                    tiles = ss14map.decode_chunk(encoded)
                except ValueError:
                    continue
                buffer = bytearray(len(tiles) * ss14map.TILE_RECORD.size)
                for position, record in enumerate(tiles):
                    ss14map.TILE_RECORD.pack_into(buffer, position * ss14map.TILE_RECORD.size, *record)
                checked += 1
                if base64.b64encode(bytes(buffer)).decode("ascii") != encoded:
                    failed += 1

    if not checked:
        return Result("chunks: round-trip vs repo maps", False, "no format-7 chunks found to check")
    return Result(
        "chunks: round-trip vs repo maps",
        failed == 0,
        f"{checked - failed}/{checked} byte-identical across {sampled} maps",
    )


def check_chunk_indexing() -> Result:
    """A tile must land in the cell it was put in, carrying all of its fields.

    Both halves matter. The cells catch an index or offset mistake; the variant
    and rotationMirroring catch a field being dropped on the way out, which is
    exactly what happened when the last byte of the record was assumed to be
    padding and 29 of this repo's chunks stopped matching.
    """
    builder = ss14map.MapBuilder("- type: MetaData", "- type: MetaData", "0.0.0")
    # position -> (variant, rotationMirroring)
    probes = {
        (0, 0): (0, 0),
        (15, 15): (1, 3),
        (16, 0): (2, 7),
        (-1, -1): (3, 1),
        (-16, 5): (1, 5),
        (3, 9): (2, 0),
    }
    for (x, y), (variant, rotation) in probes.items():
        builder.set_tile(x, y, "Plating", variant, rotation)

    tilemap = builder.build_tilemap()
    names = {number: name for name, number in tilemap.items()}
    decoded = {}
    for (chunk_x, chunk_y), encoded in builder._encode_chunks(tilemap).items():
        for index, (type_id, _, variant, rotation) in enumerate(ss14map.decode_chunk(encoded)):
            if type_id:
                x = chunk_x * ss14map.CHUNK_SIZE + index % ss14map.CHUNK_SIZE
                y = chunk_y * ss14map.CHUNK_SIZE + index // ss14map.CHUNK_SIZE
                decoded[(x, y)] = (variant, rotation)

    if decoded != probes:
        misplaced = sorted(set(probes) ^ set(decoded))
        if misplaced:
            return Result("chunks: cell indexing", False, f"cells in the wrong place: {misplaced[:6]}")
        wrong = [f"{p}: {decoded[p]} != {probes[p]}" for p in probes if decoded[p] != probes[p]]
        return Result("chunks: cell indexing", False, f"fields lost: {'; '.join(wrong[:4])}")
    return Result("chunks: cell indexing", True, f"{len(probes)} probes, negative chunks, variant and rotation kept")


def _bare_components_in(text: str) -> set[str]:
    """Component names written with no fields under them."""
    lines = text.split("\n")
    bare = set()
    for index, line in enumerate(lines):
        match = re.match(r"^(\s*)- type: (\w+)\s*$", line)
        if match is None:
            continue
        indent = len(match.group(1))
        following = lines[index + 1] if index + 1 < len(lines) else ""
        has_fields = following.strip() and (len(following) - len(following.lstrip())) > indent
        if not has_fields:
            bare.add(match.group(2))
    return bare


def check_template_components(repo_root: str, mapping_dir: str) -> Result:
    """Every component the grid template writes bare must survive being written bare.

    A component with a custom serializer can require fields that look optional.
    `GridAtmosphere` is one: written with no data it throws on load, because
    TileAtmosCollectionSerializer falls back to format 1 and demands a `tiles`
    key. The maps already in this repo are the evidence for what is safe -- if
    none of them ever writes a component bare, neither should we.
    """
    with open(os.path.join(mapping_dir, "grid_template.yml"), encoding="utf-8") as handle:
        template = yaml.safe_load(handle)

    wanted = _bare_components_in(template["map_entity"]) | _bare_components_in(template["grid_entity"])
    if not wanted:
        return Result("template: components load bare", True, "template writes no bare components")

    bare_in_maps: set[str] = set()
    seen_at_all: set[str] = set()
    maps_dir = os.path.join(repo_root, "Resources", "Maps")
    for root, _, files in os.walk(maps_dir):
        for name in files:
            if not name.endswith(".yml"):
                continue
            with open(os.path.join(root, name), encoding="utf-8") as handle:
                text = handle.read()
            bare_in_maps |= _bare_components_in(text)
            seen_at_all |= set(re.findall(r"^\s*- type: (\w+)", text, re.M))

    # Only judge components the repo's own maps actually use; anything else we
    # have no evidence about either way.
    risky = sorted(name for name in wanted & seen_at_all if name not in bare_in_maps)
    if risky:
        return Result(
            "template: components load bare",
            False,
            f"never written bare by any map in this repo, so probably needs data: {', '.join(risky)}",
        )
    return Result("template: components load bare", True, f"{len(wanted)} bare components, all seen bare in real maps")


def check_no_empty_chunks(mapping_set, index) -> Result:
    """Chunks holding nothing but Space must not be written at all.

    Space is a tile like any other in the mapping tables, so a map with a lot of
    it -- a station sitting in the middle of a 255x255 .dmm -- fills chunk after
    chunk with pure Space. The engine loads every one of them and warns about it;
    converting MetaStation once produced 143 such chunks out of 256.
    """
    builder = ss14map.MapBuilder("- type: MetaData", "- type: MetaData", "0.0.0")
    builder.set_tile(0, 0, "Plating")
    # Far enough away to land in a chunk of its own, and made of nothing.
    for offset in range(4):
        builder.set_tile(100 + offset, 100, ss14map.SPACE_TILE)

    tilemap = builder.build_tilemap()
    encoded = builder._encode_chunks(tilemap)
    empty = [
        key for key, chunk in encoded.items()
        if all(record[0] == 0 for record in ss14map.decode_chunk(chunk))
    ]
    if empty:
        return Result(
            "chunks: no empty chunks",
            False,
            f"{len(empty)} of {len(encoded)} chunks hold nothing but Space: {sorted(empty)}",
        )
    if not encoded:
        return Result("chunks: no empty chunks", False, "no chunks written at all")
    return Result("chunks: no empty chunks", True, f"{len(encoded)} chunk(s) written, the all-Space one dropped")


def check_problem_reporting(mapping_set, index) -> Result:
    """A path with no decision must be reported once, not once per code path.

    Applying the table complains that a row is blank; walking the map then
    complains that the same path has no rule. Both are true and both are the
    same problem, and adding the lists together told people 4051 paths needed
    attention when 2026 did.
    """
    paths = [
        "/obj/nothing/one",
        "/obj/nothing/two",
        "/obj/nothing/three",
    ]
    table = {path: {"dmm_path": path, "kind": "entity", "ss14_id": "", "color": ""} for path in paths}

    dmm2yml = dmm2yml_module()
    fresh = mapping_rules.load(os.path.join(os.path.dirname(os.path.abspath(__file__)), "mapping"))
    problems = dmm2yml.apply_table(table, fresh, index)

    # The same paths, now as if the walk had also found them unresolved.
    survey = dmm2yml.Survey()
    for number, path in enumerate(paths):
        survey.note_unresolved(path, "entity", number, number, {})
    problems += dmm2yml.collect_problems(survey)

    lines = dmm2yml.format_problems(problems)
    reported = [line.split(":", 1)[0] for line in lines]

    if len(reported) != len(set(reported)):
        duplicated = sorted({p for p in reported if reported.count(p) > 1})
        return Result("problems: one line per path", False, f"reported twice: {duplicated}")
    if sorted(reported) != sorted(paths):
        return Result("problems: one line per path", False, f"reported {sorted(reported)}, expected {sorted(paths)}")
    return Result("problems: one line per path", True, f"{len(paths)} unresolved paths, {len(lines)} lines")


def check_protoindex_indentation() -> Result:
    """The prototype scanner must not assume every list starts at column 0.

    Every prototype file in this repo opens its list at column 0 except one --
    Entities/Structures/Furniture/sink.yml indents the whole thing by two
    spaces, which is valid YAML (a top-level sequence only needs consistent
    indentation) but made every entity in that file invisible to the index,
    Sink included, until the scanner tracked each type marker's own indent
    instead of assuming two spaces from the left margin.
    """
    sample = (
        "  - type: entity\n"
        "    id: IndentedWidget\n"
        "    name: indented widget\n"
        "  - type: entity\n"
        "    id: IndentedAbstractWidget\n"
        "    abstract: true\n"
    )
    with tempfile.TemporaryDirectory() as directory:
        os.makedirs(os.path.join(directory, "sub"))
        with open(os.path.join(directory, "sub", "indented.yml"), "w", encoding="utf-8") as handle:
            handle.write(sample)
        index = protoindex.build(directory)

    problems = []
    if not index.has(protoindex.ENTITY, "IndentedWidget"):
        problems.append("a concrete entity in an indented list was not found")
    if "IndentedAbstractWidget" in index.entities:
        problems.append("an abstract entity in an indented list was not excluded")

    return Result(
        "dictionaries: indented prototype lists",
        not problems,
        "; ".join(problems) or "an indented '- type: entity' list still parses",
    )


def check_entity_name_quoting() -> Result:
    """A MetaData name with YAML-special characters must not corrupt the map.

    "Danger: Conveyor Access" -- a real name from a converted map -- written out
    unquoted turns `name: Danger: Conveyor Access` into a nested mapping as far
    as a YAML parser is concerned, and the whole file stops loading. Converting
    MetaStation.dmm is what surfaced this; the fixture map has no named entity
    dangerous enough to catch it on its own.
    """
    builder = ss14map.MapBuilder("- type: MetaData", "- type: MetaData", "0.0.0")
    tricky = {
        "plain": "fancy table",
        "colon": "Danger: Conveyor Access",
        "apostrophe": "O'Brien's Locker",
        "hash": "#1 Fan Club",
    }
    for name in tricky.values():
        builder.add_entity("Table", 0.5, 0.5, name=name)

    document = builder.render()
    try:
        parsed = yaml.safe_load(document)
    except yaml.YAMLError as error:
        return Result("rendering: entity names quote safely", False, f"output is not valid YAML: {error}")

    got_names = {
        c["name"]
        for group in parsed["entities"]
        if group["proto"] == "Table"
        for entity in group["entities"]
        for c in entity["components"]
        if c["type"] == "MetaData" and "name" in c
    }
    missing = set(tricky.values()) - got_names
    if missing:
        return Result("rendering: entity names quote safely", False, f"names lost or mangled: {sorted(missing)}")
    return Result("rendering: entity names quote safely", True, f"{len(tricky)} tricky names round-trip")


def run(repo_root: str, mapping_dir: str, prototypes_dir: str) -> list[Result]:
    try:
        mapping_set = mapping_rules.load(mapping_dir)
        index = protoindex.build(prototypes_dir)
    except Exception as error:
        # Nothing below can run without these; report it as one failed check
        # rather than letting the whole command crash with a bare traceback.
        return [Result("setup: mapping files and prototype index load", False, f"{type(error).__name__}: {error}")]

    checks = [
        ("parser: geometry and vars", lambda: check_parser()),
        ("rules: exact, inherited, ignored", lambda: check_rules(mapping_set)),
        ("problems: one line per path", lambda: check_problem_reporting(mapping_set, index)),
        ("orientation: wall vs dir", lambda: check_orientation(mapping_set)),
        ("chunks: cell indexing", lambda: check_chunk_indexing()),
        ("chunks: no empty chunks", lambda: check_no_empty_chunks(mapping_set, index)),
        ("chunks: round-trip vs repo maps", lambda: check_chunk_roundtrip(repo_root)),
        ("dictionaries: ids resolve", lambda: check_dictionaries(mapping_set, index)),
        ("dictionaries: indented prototype lists", lambda: check_protoindex_indentation()),
        ("conversion: fixture map", lambda: check_conversion(mapping_set, index)),
        ("rendering: valid map document", lambda: check_rendering(mapping_set, index)),
        ("rendering: entity names quote safely", lambda: check_entity_name_quoting()),
        ("template: components load bare", lambda: check_template_components(repo_root, mapping_dir)),
    ]

    results = []
    for name, check in checks:
        try:
            results.append(check())
        except Exception as error:  # a check that cannot run is a check that failed
            results.append(Result(name, False, f"{type(error).__name__}: {error}"))
    return results
