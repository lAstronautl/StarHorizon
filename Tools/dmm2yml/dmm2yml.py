#!/usr/bin/env python3
"""Convert SS13 (BYOND) ``.dmm`` maps into SS14 ``.yml`` maps.

The entity systems of the two games do not line up, so the converter never
guesses: anything it has no rule for is written into a table for a human to fill
in, and ``convert`` refuses to produce a map while that table has blanks.

    scan      read a .dmm, report every path that has no rule, as CSV
    catalog   the same, pooled across many .dmm files or whole directories
    convert   build the map, using the mapping files plus a filled-in table
    merge     fold a filled-in table back into the shared mapping files
    gui       open a window for all of the above
    selftest  run the converter's own checks against this repo

Run ``dmm2yml.py <command> --help`` for the options of each.
"""

from __future__ import annotations

import argparse
import csv
import os
import re
import sys
from collections import Counter
from dataclasses import dataclass, field

import yaml

import dmmparser
import mappings as mapping_rules
import protoindex
import ss14map

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_MAPPING_DIR = os.path.join(HERE, "mapping")
DEFAULT_PROTOTYPES = os.path.join(REPO_ROOT, "Resources", "Prototypes")

CSV_COLUMNS = ["dmm_path", "kind", "count", "example", "suggestion", "ss14_id", "color", "notes"]
MULTI_SEPARATOR = "+"
DEFAULT_DIR = 2  # BYOND entities face south unless they say otherwise

# Tile offset to walk from a floor tile onto the wall in that direction.
DIR_OFFSET = {
    mapping_rules.NORTH: (0, 1),
    mapping_rules.SOUTH: (0, -1),
    mapping_rules.EAST: (1, 0),
    mapping_rules.WEST: (-1, 0),
}


# ---------------------------------------------------------------- reporting


@dataclass
class PathReport:
    path: str
    kind: str
    count: int = 0
    example: str = ""
    variables: Counter = field(default_factory=Counter)

    @property
    def notes(self) -> str:
        return " ".join(name for name, _ in self.variables.most_common(8))


@dataclass
class Survey:
    """What one .dmm contains, and which of it the mapping files cover."""

    unresolved: dict[str, PathReport] = field(default_factory=dict)
    inherited: dict[str, str] = field(default_factory=dict)
    resolved_count: int = 0
    skipped_count: int = 0

    def note_unresolved(self, path: str, kind: str, x: int, y: int, variables, map_label: str = "") -> None:
        report = self.unresolved.get(path)
        if report is None:
            example = f"{map_label} @{x},{y}" if map_label else f"{x},{y}"
            report = self.unresolved[path] = PathReport(path=path, kind=kind, example=example)
        report.count += 1
        report.variables.update(variables.keys())

    def merge(self, other: "Survey") -> None:
        """Fold another map's survey into this one, for cataloguing several maps."""
        self.resolved_count += other.resolved_count
        self.skipped_count += other.skipped_count
        self.inherited.update(other.inherited)
        for path, report in other.unresolved.items():
            existing = self.unresolved.get(path)
            if existing is None:
                self.unresolved[path] = PathReport(
                    path=report.path, kind=report.kind, count=report.count,
                    example=report.example, variables=Counter(report.variables),
                )
            else:
                existing.count += report.count
                existing.variables.update(report.variables)


# ---------------------------------------------------------------- helpers


def guess_id(path: str, segments: int = 2) -> str:
    """Turn ``/obj/machinery/door/airlock/maintenance`` into ``AirlockMaintenance``."""
    parts = [part for part in path.strip("/").split("/") if part]
    tail = parts[-segments:] if len(parts) >= segments else parts
    return "".join(word.capitalize() for part in tail for word in part.split("_"))


def suggest_for(index: protoindex.ProtoIndex, path: str, kind: str) -> str:
    lookup_kind = {"turf": protoindex.TILE, "decal": protoindex.DECAL}.get(kind, protoindex.ENTITY)
    for segments in (2, 1, 3):
        hits = index.suggest(lookup_kind, guess_id(path, segments), limit=1)
        if hits:
            return hits[0]
    return ""


def tile_variant(tile_x: int, tile_y: int, variants: int) -> int:
    """A stable pseudo-random variant, so floors look varied but diffs stay clean."""
    if variants <= 1:
        return 0
    mixed = (tile_x * 73_856_093) ^ (tile_y * 19_349_663)
    return (mixed >> 8) % variants


def detect_engine_version(explicit: str | None) -> str:
    if explicit:
        return explicit

    props = os.path.join(REPO_ROOT, "RobustToolbox", "MSBuild", "Robust.Engine.Version.props")
    if os.path.exists(props):
        with open(props, encoding="utf-8") as handle:
            if (match := re.search(r"<Version>([^<]+)</Version>", handle.read())) is not None:
                return match.group(1).strip()

    # The submodule is often not checked out; borrow the version from a map
    # that already ships in this repo.
    reference = os.path.join(REPO_ROOT, "Resources", "Maps", "amber.yml")
    if os.path.exists(reference):
        with open(reference, encoding="utf-8") as handle:
            for line in handle:
                if (match := re.match(r"\s*engineVersion:\s*(\S+)", line)) is not None:
                    return match.group(1)
                if line.startswith("tilemap:"):
                    break
    return "0.0.0"


def direction_of(atom: dmmparser.Atom) -> int:
    value = atom.vars.get("dir", DEFAULT_DIR)
    try:
        return int(value)
    except (TypeError, ValueError):
        return DEFAULT_DIR


_HEX_COLOR = re.compile(r"^#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")


def decal_color_of(atom: dmmparser.Atom, rule: mapping_rules.DecalRule) -> str:
    """The color a decal atom should render in.

    A mapping-table rule's ``color`` is a static default, but a mapper may
    have recolored one specific instance in the .dmm with its own ``color =``
    var (BYOND hex, RGB or RGBA, no leading '#' guaranteed). Prefer that when
    it parses as a plain hex color; anything else (a named color, an rgb()
    call, a color matrix) falls back to the rule's default rather than
    guessing, same as before this override existed.
    """
    raw = atom.vars.get("color")
    if not isinstance(raw, str):
        return rule.color
    match = _HEX_COLOR.match(raw.strip())
    if not match:
        return rule.color
    hex_digits = match.group(1)
    if len(hex_digits) == 6:
        hex_digits += "FF"
    return f"#{hex_digits.upper()}"


# ---------------------------------------------------------------- the table


def read_table(path: str) -> dict[str, dict[str, str]]:
    rows: dict[str, dict[str, str]] = {}
    with open(path, encoding="utf-8-sig", newline="") as handle:
        for row in csv.DictReader(handle):
            dmm_path = (row.get("dmm_path") or "").strip()
            if dmm_path:
                rows[dmm_path] = {key: (value or "").strip() for key, value in row.items()}
    return rows


def write_table(path: str, reports: list[PathReport], index: protoindex.ProtoIndex) -> None:
    with open(path, "w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=CSV_COLUMNS)
        writer.writeheader()
        for report in sorted(reports, key=lambda r: (-r.count, r.path)):
            writer.writerow(
                {
                    "dmm_path": report.path,
                    "kind": report.kind,
                    "count": report.count,
                    "example": report.example,
                    "suggestion": suggest_for(index, report.path, report.kind),
                    "ss14_id": "",
                    "color": "",
                    "notes": report.notes,
                }
            )


def apply_table(
    table: dict[str, dict[str, str]],
    mapping_set: mapping_rules.MappingSet,
    index: protoindex.ProtoIndex,
) -> list[tuple[str, str]]:
    """Merge human decisions into the mapping set.

    Returns (path, reason) pairs rather than finished sentences, so that a path
    which is both blank here and unresolved during the walk is only reported
    once -- see `format_problems`.
    """
    problems: list[tuple[str, str]] = []

    for dmm_path, row in table.items():
        kind = row.get("kind") or "entity"
        value = row.get("ss14_id", "")

        if not value:
            problems.append((dmm_path, f"ss14_id is empty (write a prototype id, or '{mapping_rules.SKIP}')"))
            continue

        if value.lower() == mapping_rules.SKIP:
            mapping_set.ignore.append(dmm_path)
            continue

        parts = [part.strip() for part in value.split(MULTI_SEPARATOR) if part.strip()]

        if kind == "turf":
            tile = next((part for part in parts if index.has(protoindex.TILE, part)), None)
            entities = [part for part in parts if part != tile]
            unknown = [part for part in entities if not index.has(protoindex.ENTITY, part)]
            if tile is None and not entities:
                problems.append((dmm_path, f"'{value}' is neither a tile nor an entity prototype"))
                continue
            if unknown:
                problems.append((dmm_path, f"unknown entity prototype(s) {', '.join(unknown)}"))
                continue
            mapping_set.turfs[dmm_path] = mapping_rules.TurfRule(
                tile=tile, entity=entities[0] if entities else None
            )
        elif kind == "decal" and all(index.has(protoindex.DECAL, part) for part in parts):
            mapping_set.decals[dmm_path] = mapping_rules.DecalRule(
                ids=parts, color=row.get("color") or "#FFFFFFFF"
            )
        else:
            unknown = [part for part in parts if not index.has(protoindex.ENTITY, part)]
            if unknown:
                problems.append((dmm_path, f"unknown entity prototype(s) {', '.join(unknown)}"))
                continue
            mapping_set.entities[dmm_path] = mapping_rules.EntityRule(entities=parts)

    return problems


# ---------------------------------------------------------------- the walk


def walk(
    dmm: dmmparser.DmmMap,
    mapping_set: mapping_rules.MappingSet,
    index: protoindex.ProtoIndex,
    z_level: int,
    builder: ss14map.MapBuilder | None,
    variant_mode: str,
    map_label: str = "",
) -> Survey:
    """Visit every atom once; collect a survey and, if given a builder, the map."""
    survey = Survey()
    origin_x = min(x for x, _, z in dmm.grid if z == z_level)
    origin_y = min(y for _, y, z in dmm.grid if z == z_level)

    for (x, y, z), key in sorted(dmm.grid.items()):
        if z != z_level:
            continue
        tile_x, tile_y = x - origin_x, y - origin_y

        # Turfs first. A .dmm lists objects before the turf they stand on, and
        # some objects are tiles in SS14 (a lattice), so letting the turf run
        # last would paint Space back over the lattice it sits on.
        atoms = sorted(dmm.definitions[key], key=lambda a: a.kind != "turf")

        for atom in atoms:
            kind = atom.kind
            lookup_kind = kind if kind in ("turf", "decal") else "entity"
            resolution = mapping_set.resolve(atom.path, lookup_kind)

            if resolution.skipped:
                survey.skipped_count += 1
                continue
            if resolution.rule is None:
                survey.note_unresolved(atom.path, kind, x, y, atom.vars, map_label)
                continue

            survey.resolved_count += 1
            if resolution.matched_path and not resolution.exact:
                survey.inherited[atom.path] = resolution.matched_path
            if builder is None:
                continue

            direction = direction_of(atom)
            rule = resolution.rule

            if kind == "turf":
                if rule.tile:
                    variants = index.variants(rule.tile)
                    variant = 0 if variant_mode == "zero" else tile_variant(tile_x, tile_y, variants)
                    builder.set_tile(tile_x, tile_y, rule.tile, variant)
                if rule.entity:
                    builder.add_entity(rule.entity, tile_x + 0.5, tile_y + 0.5)
            elif kind == "decal" and isinstance(rule, mapping_rules.DecalRule):
                color = decal_color_of(atom, rule)
                for decal_id in rule.ids_for(direction):
                    builder.add_decal(
                        tile_x,
                        tile_y,
                        ss14map.DecalNode(
                            decal_id=decal_id,
                            color=color,
                            angle=rule.angle,
                            z_index=rule.z_index,
                            cleanable=rule.cleanable,
                        ),
                    )
            else:
                name = atom.vars.get("name")
                if rule.tile:
                    variants = index.variants(rule.tile)
                    variant = 0 if variant_mode == "zero" else tile_variant(tile_x, tile_y, variants)
                    builder.set_tile(tile_x, tile_y, rule.tile, variant)

                # A dir on the dmm atom does not always mean "facing" -- a
                # disposal pipe segment with a diagonal dir is BYOND's way of
                # saying "this is a bend", a different prototype entirely. See
                # EntityRule.by_dir.
                variant_rule = rule.by_dir.get(direction)
                entities = variant_rule.entities if variant_rule is not None else rule.entities
                if variant_rule is not None and variant_rule.direction is not None:
                    facing = variant_rule.direction
                elif rule.direction is not None:
                    facing = rule.direction
                else:
                    facing = direction

                # SS13 leaves a wall-mounted object on the room's floor tile; SS14
                # embeds it in the wall's own tile instead. Walk one tile against
                # the facing direction (i.e. toward the wall it is mounted on) to
                # land on the same tile the wall occupies. See EntityRule.on_wall.
                entity_x, entity_y = tile_x, tile_y
                if rule.on_wall:
                    offset_x, offset_y = DIR_OFFSET.get(facing, (0, 0))
                    entity_x -= offset_x
                    entity_y -= offset_y

                for proto in entities:
                    builder.add_entity(
                        proto,
                        entity_x + 0.5,
                        entity_y + 0.5,
                        rotation=ss14map.rotation_for_dir(facing),
                        name=str(name) if isinstance(name, str) else None,
                    )

    return survey


# ---------------------------------------------------------------- commands


def load_context(args) -> tuple[mapping_rules.MappingSet, protoindex.ProtoIndex]:
    mapping_set = mapping_rules.load(args.mapping_dir)
    index = protoindex.build(args.prototypes)
    return mapping_set, index


def collect_dmm_files(paths: list[str]) -> list[str]:
    """Files named directly, plus every .dmm found by walking any directories given."""
    files: list[str] = []
    for path in paths:
        if os.path.isdir(path):
            for root, _, names in os.walk(path):
                for name in sorted(names):
                    if name.endswith(".dmm"):
                        files.append(os.path.join(root, name))
        elif path.endswith(".dmm"):
            files.append(path)
        else:
            print(f"skipping {path}: not a .dmm file or a directory", file=sys.stderr)
    return sorted(set(files))


def command_catalog(args) -> int:
    """Scan several .dmm files at once and pool what none of them resolve.

    One map only shows the paths it happens to use. A whole upstream map
    catalogue -- tgstation ships eight -- shows what a converter actually needs
    to cover, ranked by how often each path is really placed, rather than by
    how large any single station happens to be.
    """
    mapping_set, index = load_context(args)
    files = collect_dmm_files(args.paths)
    if not files:
        print("no .dmm files found under the given paths", file=sys.stderr)
        return 2

    merged = Survey()
    scanned = 0
    for path in files:
        try:
            dmm = dmmparser.parse(path)
        except dmmparser.DmmParseError as error:
            print(f"skipping {path}: {error}", file=sys.stderr)
            continue
        label = os.path.splitext(os.path.basename(path))[0]
        for z_level in dmm.z_levels:
            merged.merge(walk(dmm, mapping_set, index, z_level, None, args.variants, map_label=label))
        scanned += 1

    print(f"{scanned} map(s) scanned ({len(files) - scanned} skipped)")
    print(f"  resolved   {merged.resolved_count} atoms")
    print(f"  skipped    {merged.skipped_count} atoms (ignore rules)")
    print(
        f"  unresolved {sum(r.count for r in merged.unresolved.values())} atoms "
        f"across {len(merged.unresolved)} distinct paths"
    )

    write_table(args.output, list(merged.unresolved.values()), index)
    print(f"\nwrote {args.output}")
    return 0


def command_scan(args) -> int:
    dmm = dmmparser.parse(args.dmm)
    mapping_set, index = load_context(args)

    if args.all_z:
        survey = Survey()
        for z_level in dmm.z_levels:
            survey.merge(walk(dmm, mapping_set, index, z_level, None, args.variants, map_label=f"z{z_level}"))
        scanned = f"z-levels {dmm.z_levels} (all of them)"
    else:
        z_level = args.z if args.z is not None else dmm.z_levels[0]
        survey = walk(dmm, mapping_set, index, z_level, None, args.variants)
        scanned = f"z-levels {dmm.z_levels} (scanning z={z_level})"
        if len(dmm.z_levels) > 1 and args.z is None:
            print(
                f"note: this map has {len(dmm.z_levels)} z-levels ({dmm.z_levels}); only z={z_level} was "
                f"scanned. Pass --z N for a specific level or --all-z to pool every level into one table.",
                file=sys.stderr,
            )

    print(f"{args.dmm}: {dmm.width}x{dmm.height}, {scanned}")
    print(f"  resolved   {survey.resolved_count} atoms")
    print(f"  skipped    {survey.skipped_count} atoms (ignore rules)")
    print(f"  unresolved {sum(r.count for r in survey.unresolved.values())} atoms "
          f"across {len(survey.unresolved)} distinct paths")
    if survey.inherited:
        print(f"  {len(survey.inherited)} paths matched a parent rule rather than an exact one")

    write_table(args.output, list(survey.unresolved.values()), index)
    print(f"\nwrote {args.output} -- fill in the ss14_id column, then run 'convert --table'.")
    print(f"Write '{mapping_rules.SKIP}' in ss14_id to drop a path on purpose.")
    return 0


def collect_problems(survey: Survey) -> list[tuple[str, str]]:
    """(path, reason) for every path that still has no decision, worst first."""
    return [
        (report.path, f"no rule ({report.count} uses, e.g. at {report.example})")
        for report in sorted(survey.unresolved.values(), key=lambda r: -r.count)
    ]


def format_problems(problems: list[tuple[str, str]]) -> list[str]:
    """One line per path.

    A blank table row and an unresolved path are the same problem seen twice --
    once when the table is applied and again when the map is walked. Reporting
    both told people 4051 paths needed attention when 2026 did.
    """
    seen: dict[str, str] = {}
    for path, reason in problems:
        seen.setdefault(path, reason)
    return [f"{path}: {reason}" for path, reason in seen.items()]


def build_map(
    dmm: dmmparser.DmmMap,
    mapping_set: mapping_rules.MappingSet,
    index: protoindex.ProtoIndex,
    z_level: int,
    variant_mode: str,
    mapping_dir: str,
    engine_version: str | None = None,
) -> ss14map.MapBuilder:
    """Walk the map into a builder, ready to render."""
    with open(os.path.join(mapping_dir, "grid_template.yml"), encoding="utf-8") as handle:
        template = yaml.safe_load(handle)

    builder = ss14map.MapBuilder(
        map_entity_template=template["map_entity"],
        grid_entity_template=template["grid_entity"],
        engine_version=detect_engine_version(engine_version),
    )
    walk(dmm, mapping_set, index, z_level, builder, variant_mode)
    return builder


def describe_map(builder: ss14map.MapBuilder, log=print) -> None:
    log(f"  tiles    {len(builder.tiles)} ({len(builder.build_tilemap())} distinct)")
    log(f"  decals   {sum(len(v) for v in builder.decals.values())} in {len(builder.decals)} nodes")
    log(f"  entities {len(builder.entities)}")
    report_disconnected(builder, log)


def per_z_output_path(output: str, z_level: int) -> str:
    """map.yml, z 2 -> map.z2.yml -- SS14 has no stacked floors, so --all-z writes one file per level."""
    stem, ext = os.path.splitext(output)
    return f"{stem}.z{z_level}{ext or '.yml'}"


def _report_refusal(lines: list[str]) -> None:
    print(f"Refusing to convert: {len(lines)} path(s) still need a decision.\n", file=sys.stderr)
    for line in lines[:20]:
        print(f"  {line}", file=sys.stderr)
    if len(lines) > 20:
        print(f"  ... and {len(lines) - 20} more", file=sys.stderr)
    print(
        f"\nRun 'scan' to regenerate the table, fill in ss14_id for every row "
        f"(or '{mapping_rules.SKIP}' to drop it), then convert again.",
        file=sys.stderr,
    )


def command_convert(args) -> int:
    dmm = dmmparser.parse(args.dmm)
    mapping_set, index = load_context(args)

    if not args.all_z and args.z is None and len(dmm.z_levels) > 1:
        print(
            f"{args.dmm} has {len(dmm.z_levels)} z-levels ({dmm.z_levels}), and SS14 has no stacked floors -- "
            f"each one needs to become its own map. Pick one with --z N, or convert all of them at once "
            f"with --all-z (writes one file per level, named from -o).",
            file=sys.stderr,
        )
        return 1

    z_levels = list(dmm.z_levels) if args.all_z else [args.z if args.z is not None else dmm.z_levels[0]]

    problems: list[tuple[str, str]] = []
    if args.table:
        problems = apply_table(read_table(args.table), mapping_set, index)

    survey = Survey()
    for z_level in z_levels:
        label = f"z{z_level}" if args.all_z else ""
        survey.merge(walk(dmm, mapping_set, index, z_level, None, args.variants, map_label=label))
    problems += collect_problems(survey)
    lines = format_problems(problems)

    if lines:
        _report_refusal(lines)
        return 1

    exit_code = 0
    for z_level in z_levels:
        output = per_z_output_path(args.output, z_level) if args.all_z else args.output
        builder = build_map(dmm, mapping_set, index, z_level, args.variants, args.mapping_dir, args.engine_version)
        with open(output, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(builder.render())

        print(f"wrote {output}" + (f" (z={z_level})" if args.all_z else ""))
        describe_map(builder)
        if not args.no_verify:
            exit_code = verify(builder) or exit_code
    return exit_code


def report_disconnected(builder: ss14map.MapBuilder, log=print) -> None:
    """Warn about tile islands, because the engine will split them into grids.

    SS14 gives every disconnected run of tiles its own grid. That is correct, but
    seeing it for the first time in the server log as "Splitting grid into 2
    grids" is a surprise; better to say so while the mapper is still looking.
    """
    tiles = {
        position
        for position, (tile_id, _, _) in builder.tiles.items()
        if tile_id != ss14map.SPACE_TILE
    }

    seen: set[tuple[int, int]] = set()
    regions: list[list[tuple[int, int]]] = []
    for start in tiles:
        if start in seen:
            continue
        stack = [start]
        seen.add(start)
        region = []
        while stack:
            x, y = stack.pop()
            region.append((x, y))
            for neighbour in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if neighbour in tiles and neighbour not in seen:
                    seen.add(neighbour)
                    stack.append(neighbour)
        regions.append(region)

    if len(regions) <= 1:
        return

    regions.sort(key=len, reverse=True)
    log(f"  note     {len(regions)} disconnected tile regions -- SS14 will load these as separate grids:")
    for region in regions[1:6]:
        x = sum(position[0] for position in region) / len(region)
        y = sum(position[1] for position in region) / len(region)
        log(f"             {len(region)} tiles around {x:.0f},{y:.0f}")
    if len(regions) > 6:
        log(f"             ... and {len(regions) - 6} more")


def verify(builder: ss14map.MapBuilder, log=print) -> int:
    """Decode the chunks we just encoded and check they say what we meant.

    This is the check that catches a chunk-index or byte-offset mistake, which
    would otherwise show up only as a map that looks subtly scrambled in-game.
    """
    tilemap = builder.build_tilemap()
    by_number = {number: name for name, number in tilemap.items()}

    decoded: dict[tuple[int, int], tuple[str, int]] = {}
    for (chunk_x, chunk_y), encoded in builder._encode_chunks(tilemap).items():
        for index, (type_id, _, variant, _) in enumerate(ss14map.decode_chunk(encoded)):
            if type_id == 0:
                continue  # Space: either deliberate, or an untouched cell
            tile_x = chunk_x * ss14map.CHUNK_SIZE + index % ss14map.CHUNK_SIZE
            tile_y = chunk_y * ss14map.CHUNK_SIZE + index // ss14map.CHUNK_SIZE
            decoded[(tile_x, tile_y)] = (by_number[type_id], variant)

    expected = {
        position: (tile_id, variant)
        for position, (tile_id, variant, _) in builder.tiles.items()
        if tile_id != ss14map.SPACE_TILE
    }

    if decoded != expected:
        missing = set(expected) - set(decoded)
        extra = set(decoded) - set(expected)
        wrong = [p for p in set(expected) & set(decoded) if expected[p] != decoded[p]]
        log(
            f"  VERIFY FAILED: {len(missing)} tiles lost, {len(extra)} invented, "
            f"{len(wrong)} wrong after encoding"
        )
        return 1

    log(f"  verify   OK ({len(expected)} tiles survive the chunk round-trip)")
    return 0


def merge_table(
    table: dict[str, dict[str, str]],
    mapping_dir: str,
    index: protoindex.ProtoIndex,
    log=print,
) -> int:
    """Append the decisions in `table` to the shared mapping files. Returns how many."""
    additions: dict[str, dict] = {"turf": {}, "decal": {}, "entity": {}}
    skips: list[str] = []

    for dmm_path, row in table.items():
        kind = row.get("kind") or "entity"
        value = (row.get("ss14_id") or "").strip()
        if not value:
            continue
        if value.lower() == mapping_rules.SKIP:
            skips.append(dmm_path)
        elif kind == "turf":
            additions["turf"][dmm_path] = value
        elif kind == "decal" and all(
            index.has(protoindex.DECAL, part) for part in value.split(MULTI_SEPARATOR)
        ):
            entry: dict = (
                {"id": value} if MULTI_SEPARATOR not in value
                else {"decals": [part.strip() for part in value.split(MULTI_SEPARATOR)]}
            )
            if row.get("color"):
                entry["color"] = row["color"]
            additions["decal"][dmm_path] = entry
        else:
            additions["entity"][dmm_path] = value

    written = 0
    for name, key in (("turfs.yml", "turf"), ("decals.yml", "decal"), ("entities.yml", "entity")):
        if not additions[key]:
            continue
        path = os.path.join(mapping_dir, name)
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("\n# Added by dmm2yml merge\n")
            handle.write(yaml.safe_dump(additions[key], allow_unicode=True, sort_keys=True, default_flow_style=False))
        log(f"appended {len(additions[key])} rule(s) to {path}")
        written += len(additions[key])

    if skips:
        path = os.path.join(mapping_dir, "ignore.yml")
        with open(path, "a", encoding="utf-8") as handle:
            handle.write("\n# Added by dmm2yml merge\n")
            handle.write(yaml.safe_dump(sorted(skips), allow_unicode=True, default_flow_style=False))
        log(f"appended {len(skips)} ignore rule(s) to {path}")
        written += len(skips)

    return written


def command_merge(args) -> int:
    mapping_set, index = load_context(args)
    table = read_table(args.table)
    lines = format_problems(apply_table(table, mapping_set, index))
    if lines:
        print(f"{len(lines)} row(s) could not be applied:", file=sys.stderr)
        for line in lines[:20]:
            print(f"  {line}", file=sys.stderr)
        return 1

    merge_table(table, args.mapping_dir, index)
    return 0


def command_gui(args) -> int:
    import gui

    return gui.main(
        [
            "--mapping-dir", args.mapping_dir,
            "--prototypes", args.prototypes,
            "--variants", args.variants,
        ]
        + (["--z", str(args.z)] if args.z is not None else [])
        + (["--dmm", args.dmm] if getattr(args, "dmm", None) else [])
    )


def command_selftest(args) -> int:
    """Run every check and print a line per check."""
    import selftest

    results = selftest.run(REPO_ROOT, args.mapping_dir, args.prototypes)
    width = max(len(result.name) for result in results)
    for result in results:
        status = "ok  " if result.passed else "FAIL"
        print(f"  [{status}] {result.name.ljust(width)}  {result.detail}")

    failed = [result for result in results if not result.passed]
    print()
    if failed:
        print(f"{len(failed)} of {len(results)} checks failed.")
        return 1
    print(f"all {len(results)} checks passed.")
    return 0


# ---------------------------------------------------------------- entry


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="dmm2yml.py", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    subparsers = parser.add_subparsers(dest="command", required=True)

    def add_common(sub, needs_dmm=True):
        if needs_dmm:
            sub.add_argument("dmm", help="the .dmm file to read")
        sub.add_argument("--mapping-dir", default=DEFAULT_MAPPING_DIR, help="directory holding the mapping files")
        sub.add_argument("--prototypes", default=DEFAULT_PROTOTYPES, help="Resources/Prototypes to validate against")
        sub.add_argument("--z", type=int, default=None, help="z-level to convert (default: the first one)")
        sub.add_argument(
            "--all-z", action="store_true",
            help="every z-level, not just one -- SS14 has no stacked floors, so each becomes its own map/file",
        )
        sub.add_argument("--variants", choices=("deterministic", "zero"), default="deterministic",
                         help="tile variant picking (default: deterministic)")

    scan = subparsers.add_parser("scan", help="report paths that have no rule, as CSV")
    add_common(scan)
    scan.add_argument("-o", "--output", required=True, help="CSV file to write")
    scan.set_defaults(func=command_scan)

    catalog = subparsers.add_parser(
        "catalog", help="scan several .dmm files (or whole directories of them) at once"
    )
    catalog.add_argument("paths", nargs="+", help=".dmm files and/or directories to search recursively")
    catalog.add_argument("--mapping-dir", default=DEFAULT_MAPPING_DIR, help="directory holding the mapping files")
    catalog.add_argument("--prototypes", default=DEFAULT_PROTOTYPES, help="Resources/Prototypes to validate against")
    catalog.add_argument("--variants", choices=("deterministic", "zero"), default="deterministic")
    catalog.add_argument("-o", "--output", required=True, help="CSV file to write")
    catalog.set_defaults(func=command_catalog)

    convert = subparsers.add_parser("convert", help="build the SS14 map")
    add_common(convert)
    convert.add_argument("-o", "--output", required=True, help=".yml map file to write")
    convert.add_argument("--table", help="filled-in CSV from 'scan'")
    convert.add_argument("--engine-version", help="value for meta.engineVersion")
    convert.add_argument("--no-verify", action="store_true", help="skip the tile round-trip check")
    convert.set_defaults(func=command_convert)

    merge = subparsers.add_parser("merge", help="fold a filled-in table into the mapping files")
    add_common(merge, needs_dmm=False)
    merge.add_argument("table", help="filled-in CSV from 'scan'")
    merge.set_defaults(func=command_merge)

    gui_parser = subparsers.add_parser("gui", help="open the window")
    add_common(gui_parser, needs_dmm=False)
    gui_parser.add_argument("--dmm", help="open this map straight away")
    gui_parser.set_defaults(func=command_gui)

    selftest_parser = subparsers.add_parser(
        "selftest", help="run the converter's own checks against this repo"
    )
    selftest_parser.add_argument("--mapping-dir", default=DEFAULT_MAPPING_DIR,
                                 help="directory holding the mapping files")
    selftest_parser.add_argument("--prototypes", default=DEFAULT_PROTOTYPES,
                                 help="Resources/Prototypes to validate against")
    selftest_parser.set_defaults(func=command_selftest)

    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (dmmparser.DmmParseError, mapping_rules.MappingError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
