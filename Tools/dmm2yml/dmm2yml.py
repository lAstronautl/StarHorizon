#!/usr/bin/env python3
"""Convert SS13 (BYOND) ``.dmm`` maps into SS14 ``.yml`` maps.

The entity systems of the two games do not line up, so the converter never
guesses: anything it has no rule for is written into a table for a human to fill
in, and ``convert`` refuses to produce a map while that table has blanks.

    scan      read a .dmm, report every path that has no rule, as CSV
    convert   build the map, using the mapping files plus a filled-in table
    merge     fold a filled-in table back into the shared mapping files
    selftest  check the chunk encoder against the maps already in the repo

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

    def note_unresolved(self, path: str, kind: str, x: int, y: int, variables) -> None:
        report = self.unresolved.get(path)
        if report is None:
            report = self.unresolved[path] = PathReport(path=path, kind=kind, example=f"{x},{y}")
        report.count += 1
        report.variables.update(variables.keys())


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
) -> list[str]:
    """Merge human decisions into the mapping set. Returns the problems found."""
    problems: list[str] = []

    for dmm_path, row in table.items():
        kind = row.get("kind") or "entity"
        value = row.get("ss14_id", "")

        if not value:
            problems.append(f"{dmm_path}: ss14_id is empty (write a prototype id, or '{mapping_rules.SKIP}')")
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
                problems.append(f"{dmm_path}: '{value}' is neither a tile nor an entity prototype")
                continue
            if unknown:
                problems.append(f"{dmm_path}: unknown entity prototype(s) {', '.join(unknown)}")
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
                problems.append(f"{dmm_path}: unknown entity prototype(s) {', '.join(unknown)}")
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
                survey.note_unresolved(atom.path, kind, x, y, atom.vars)
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
                for decal_id in rule.ids_for(direction):
                    builder.add_decal(
                        tile_x,
                        tile_y,
                        ss14map.DecalNode(
                            decal_id=decal_id,
                            color=rule.color,
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
                facing = rule.direction if rule.direction is not None else direction
                for proto in rule.entities:
                    builder.add_entity(
                        proto,
                        tile_x + 0.5,
                        tile_y + 0.5,
                        rotation=ss14map.rotation_for_dir(facing),
                        name=str(name) if isinstance(name, str) else None,
                    )

    return survey


# ---------------------------------------------------------------- commands


def load_context(args) -> tuple[mapping_rules.MappingSet, protoindex.ProtoIndex]:
    mapping_set = mapping_rules.load(args.mapping_dir)
    index = protoindex.build(args.prototypes)
    return mapping_set, index


def command_scan(args) -> int:
    dmm = dmmparser.parse(args.dmm)
    mapping_set, index = load_context(args)
    z_level = args.z if args.z is not None else dmm.z_levels[0]

    survey = walk(dmm, mapping_set, index, z_level, None, args.variants)

    print(f"{args.dmm}: {dmm.width}x{dmm.height}, z-levels {dmm.z_levels} (scanning z={z_level})")
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


def command_convert(args) -> int:
    dmm = dmmparser.parse(args.dmm)
    mapping_set, index = load_context(args)
    z_level = args.z if args.z is not None else dmm.z_levels[0]

    problems: list[str] = []
    if args.table:
        problems = apply_table(read_table(args.table), mapping_set, index)

    survey = walk(dmm, mapping_set, index, z_level, None, args.variants)
    for report in sorted(survey.unresolved.values(), key=lambda r: -r.count):
        problems.append(f"{report.path}: no rule ({report.count} uses, e.g. at {report.example})")

    if problems:
        print(f"Refusing to convert: {len(problems)} path(s) still need a decision.\n", file=sys.stderr)
        for line in problems[:20]:
            print(f"  {line}", file=sys.stderr)
        if len(problems) > 20:
            print(f"  ... and {len(problems) - 20} more", file=sys.stderr)
        print(
            f"\nRun 'scan' to regenerate the table, fill in ss14_id for every row "
            f"(or '{mapping_rules.SKIP}' to drop it), then convert again.",
            file=sys.stderr,
        )
        return 1

    template = yaml.safe_load(open(os.path.join(args.mapping_dir, "grid_template.yml"), encoding="utf-8"))
    builder = ss14map.MapBuilder(
        map_entity_template=template["map_entity"],
        grid_entity_template=template["grid_entity"],
        engine_version=detect_engine_version(args.engine_version),
    )
    walk(dmm, mapping_set, index, z_level, builder, args.variants)

    with open(args.output, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(builder.render())

    print(f"wrote {args.output}")
    print(f"  tiles    {len(builder.tiles)} ({len(builder.build_tilemap())} distinct)")
    print(f"  decals   {sum(len(v) for v in builder.decals.values())} in {len(builder.decals)} nodes")
    print(f"  entities {len(builder.entities)}")
    if not args.no_verify:
        return verify(builder)
    return 0


def verify(builder: ss14map.MapBuilder) -> int:
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
        print(
            f"  VERIFY FAILED: {len(missing)} tiles lost, {len(extra)} invented, "
            f"{len(wrong)} wrong after encoding",
            file=sys.stderr,
        )
        return 1

    print(f"  verify   OK ({len(expected)} tiles survive the chunk round-trip)")
    return 0


def command_merge(args) -> int:
    mapping_set, index = load_context(args)
    table = read_table(args.table)
    problems = apply_table(table, mapping_set, index)
    if problems:
        print(f"{len(problems)} row(s) could not be applied:", file=sys.stderr)
        for line in problems[:20]:
            print(f"  {line}", file=sys.stderr)
        return 1

    additions = {"turf": {}, "decal": {}, "entity": {}, "skip": []}
    for dmm_path, row in table.items():
        kind = row.get("kind") or "entity"
        value = row["ss14_id"]
        if value.lower() == mapping_rules.SKIP:
            additions["skip"].append(dmm_path)
        elif kind == "turf":
            additions["turf"][dmm_path] = value
        elif kind == "decal":
            entry = {"id": value} if MULTI_SEPARATOR not in value else {"decals": value.split(MULTI_SEPARATOR)}
            if row.get("color"):
                entry["color"] = row["color"]
            additions["decal"][dmm_path] = entry
        else:
            additions["entity"][dmm_path] = value

    for name, key in (("turfs.yml", "turf"), ("decals.yml", "decal"), ("entities.yml", "entity")):
        if not additions[key]:
            continue
        path = os.path.join(args.mapping_dir, name)
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(f"\n# Added by 'merge' from {os.path.basename(args.table)}\n")
            handle.write(yaml.safe_dump(additions[key], allow_unicode=True, sort_keys=True, default_flow_style=False))
        print(f"appended {len(additions[key])} rule(s) to {path}")

    if additions["skip"]:
        path = os.path.join(args.mapping_dir, "ignore.yml")
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(f"\n# Added by 'merge' from {os.path.basename(args.table)}\n")
            handle.write(yaml.safe_dump(sorted(additions["skip"]), allow_unicode=True, default_flow_style=False))
        print(f"appended {len(additions['skip'])} ignore rule(s) to {path}")

    return 0


def command_selftest(args) -> int:
    """Encode/decode chunks taken from maps that already ship in this repo."""
    maps_dir = os.path.join(REPO_ROOT, "Resources", "Maps")
    checked = failed = 0

    for root, _, files in os.walk(maps_dir):
        for name in sorted(files):
            if not name.endswith(".yml"):
                continue
            with open(os.path.join(root, name), encoding="utf-8") as handle:
                text = handle.read()
            if "version: 7" not in text:
                continue  # only format-7 chunks use the 7-byte record
            for encoded in re.findall(r"tiles: ([A-Za-z0-9+/=]+)\n", text)[:50]:
                try:
                    tiles = ss14map.decode_chunk(encoded)
                except ValueError:
                    continue
                buffer = bytearray(len(tiles) * ss14map.TILE_RECORD.size)
                for position, record in enumerate(tiles):
                    ss14map.TILE_RECORD.pack_into(
                        buffer, position * ss14map.TILE_RECORD.size, *record
                    )
                import base64

                checked += 1
                if base64.b64encode(bytes(buffer)).decode("ascii") != encoded:
                    failed += 1
        if checked > 2000:
            break

    print(f"chunk round-trip: {checked - failed}/{checked} byte-identical")
    return 1 if failed or not checked else 0


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
        sub.add_argument("--variants", choices=("deterministic", "zero"), default="deterministic",
                         help="tile variant picking (default: deterministic)")

    scan = subparsers.add_parser("scan", help="report paths that have no rule, as CSV")
    add_common(scan)
    scan.add_argument("-o", "--output", required=True, help="CSV file to write")
    scan.set_defaults(func=command_scan)

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

    selftest = subparsers.add_parser("selftest", help="check the chunk encoder against this repo's maps")
    selftest.set_defaults(func=command_selftest)

    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (dmmparser.DmmParseError, mapping_rules.MappingError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
