"""Writer for the SS14 map format (``format: 7``).

The output is assembled line by line rather than handed to a YAML library on
purpose: SS14 saves maps with a very specific shape (key order inside decal
nodes, ``'#RRGGBBAA'`` quoting, two-space indents, sequences at the parent's
indent).  Matching it means that opening a converted map in the SS14 editor and
saving it again produces a near-empty diff, which is what makes the output
reviewable.
"""

from __future__ import annotations

import base64
import struct
from dataclasses import dataclass, field
from datetime import datetime, timezone

CHUNK_SIZE = 16
TILES_PER_CHUNK = CHUNK_SIZE * CHUNK_SIZE
MAP_FORMAT = 7
CHUNK_VERSION = 7
DECAL_COLLECTION_VERSION = 2

# typeId (uint32 LE), flags, variant, rotationMirroring -- 7 bytes.
# Verified against this repo's own maps: amber.yml leaves the last byte at 0,
# but exo.yml uses values 0-7 there, so it is a real field and must round-trip.
TILE_RECORD = struct.Struct("<IBBB")

SPACE_TILE = "Space"

MAP_ENTITY_UID = 1
GRID_ENTITY_UID = 2
FIRST_FREE_UID = 3

# BYOND dir -> SS14 Transform rotation.  Verified statistically against the
# walls surrounding wall-mounted machines in Resources/Maps/amber.yml.
NORTH, SOUTH, EAST, WEST = 1, 2, 4, 8
DIR_TO_ROTATION = {
    SOUTH: None,  # rot 0 is the default and is omitted entirely
    EAST: 1.5707963267948966,
    NORTH: 3.141592653589793,
    WEST: -1.5707963267948966,
}
# Diagonals have no SS14 equivalent; they collapse onto one cardinal component.
DIR_TO_ROTATION.update(
    {
        NORTH | EAST: DIR_TO_ROTATION[NORTH],
        NORTH | WEST: DIR_TO_ROTATION[NORTH],
        SOUTH | EAST: DIR_TO_ROTATION[SOUTH],
        SOUTH | WEST: DIR_TO_ROTATION[SOUTH],
    }
)


def rotation_for_dir(direction: int | None) -> float | None:
    if direction is None:
        return None
    return DIR_TO_ROTATION.get(int(direction), None)


def number(value: float) -> str:
    """Render a coordinate the way SS14 does: ``-8`` stays an int, ``8.5`` does not."""
    if isinstance(value, int) or float(value).is_integer():
        return str(int(value))
    return repr(round(float(value), 6))


@dataclass(frozen=True)
class DecalNode:
    decal_id: str
    color: str = "#FFFFFFFF"
    angle: float | None = None
    z_index: int | None = None
    cleanable: bool | None = None


@dataclass
class Entity:
    uid: int
    proto: str
    x: float
    y: float
    rotation: float | None = None
    name: str | None = None


@dataclass
class MapBuilder:
    """Collects tiles, decals and entities, then renders one map document."""

    map_entity_template: str
    grid_entity_template: str
    engine_version: str = "0.0.0"

    tiles: dict[tuple[int, int], tuple[str, int, int]] = field(default_factory=dict)
    decals: dict[DecalNode, list[tuple[float, float]]] = field(default_factory=dict)
    entities: list[Entity] = field(default_factory=list)
    _next_uid: int = FIRST_FREE_UID

    # -- collection ------------------------------------------------------

    def set_tile(
        self,
        tile_x: int,
        tile_y: int,
        tile_id: str,
        variant: int = 0,
        rotation_mirroring: int = 0,
    ) -> None:
        self.tiles[(tile_x, tile_y)] = (tile_id, variant, rotation_mirroring)

    def add_decal(self, x: float, y: float, node: DecalNode) -> None:
        self.decals.setdefault(node, []).append((x, y))

    def add_entity(
        self,
        proto: str,
        x: float,
        y: float,
        rotation: float | None = None,
        name: str | None = None,
    ) -> int:
        uid = self._next_uid
        self._next_uid += 1
        self.entities.append(Entity(uid, proto, x, y, rotation, name))
        return uid

    @property
    def entity_count(self) -> int:
        return len(self.entities) + 2  # the map and grid entities

    # -- tile encoding ---------------------------------------------------

    def build_tilemap(self) -> dict[str, int]:
        """Assign a numeric id to every tile prototype used, with Space at 0."""
        used = {tile_id for tile_id, _, _ in self.tiles.values()}
        used.discard(SPACE_TILE)
        tilemap = {SPACE_TILE: 0}
        for index, tile_id in enumerate(sorted(used), start=1):
            tilemap[tile_id] = index
        return tilemap

    def _encode_chunks(self, tilemap: dict[str, int]) -> dict[tuple[int, int], str]:
        chunks: dict[tuple[int, int], bytearray] = {}
        for (tile_x, tile_y), (tile_id, variant, rotation_mirroring) in self.tiles.items():
            chunk_key = (tile_x // CHUNK_SIZE, tile_y // CHUNK_SIZE)
            if (buffer := chunks.get(chunk_key)) is None:
                # An unwritten tile is Space, which is type id 0 -- all zeroes.
                buffer = chunks[chunk_key] = bytearray(TILES_PER_CHUNK * TILE_RECORD.size)

            local_x = tile_x - chunk_key[0] * CHUNK_SIZE
            local_y = tile_y - chunk_key[1] * CHUNK_SIZE
            offset = (local_y * CHUNK_SIZE + local_x) * TILE_RECORD.size
            TILE_RECORD.pack_into(buffer, offset, tilemap[tile_id], 0, variant, rotation_mirroring)

        return {key: base64.b64encode(bytes(buffer)).decode("ascii") for key, buffer in chunks.items()}

    # -- rendering -------------------------------------------------------

    def _render_chunks(self, tilemap: dict[str, int]) -> list[str]:
        lines: list[str] = []
        for (chunk_x, chunk_y), encoded in sorted(self._encode_chunks(tilemap).items()):
            lines.append(f"        {chunk_x},{chunk_y}:")
            lines.append(f"          ind: {chunk_x},{chunk_y}")
            lines.append(f"          tiles: {encoded}")
            lines.append(f"          version: {CHUNK_VERSION}")
        return lines

    def _render_decals(self) -> list[str]:
        if not self.decals:
            return ["        nodes: []"]

        lines = ["        nodes:"]
        decal_uid = 0
        ordered = sorted(
            self.decals.items(),
            key=lambda item: (
                item[0].decal_id,
                item[0].color,
                item[0].angle if item[0].angle is not None else -99.0,
                item[0].z_index if item[0].z_index is not None else -1,
                item[0].cleanable is True,
            ),
        )
        for node, positions in ordered:
            lines.append("        - node:")
            # Key order copied from SS14's own output.
            if node.cleanable is not None:
                lines.append(f"            cleanable: {'True' if node.cleanable else 'False'}")
            if node.z_index is not None:
                lines.append(f"            zIndex: {node.z_index}")
            if node.angle is not None:
                lines.append(f"            angle: {node.angle} rad")
            lines.append(f"            color: '{node.color}'")
            lines.append(f"            id: {node.decal_id}")
            lines.append("          decals:")
            for x, y in sorted(positions):
                lines.append(f"            {decal_uid}: {number(x)},{number(y)}")
                decal_uid += 1
        return lines

    def _render_entities(self) -> list[str]:
        by_proto: dict[str, list[Entity]] = {}
        for entity in self.entities:
            by_proto.setdefault(entity.proto, []).append(entity)

        lines: list[str] = []
        for proto in sorted(by_proto):
            lines.append(f"- proto: {proto}")
            lines.append("  entities:")
            for entity in sorted(by_proto[proto], key=lambda e: e.uid):
                lines.append(f"  - uid: {entity.uid}")
                lines.append("    components:")
                if entity.name:
                    lines.append("    - type: MetaData")
                    lines.append(f"      name: {entity.name}")
                lines.append("    - type: Transform")
                if entity.rotation is not None:
                    lines.append(f"      rot: {entity.rotation} rad")
                lines.append(f"      pos: {number(entity.x)},{number(entity.y)}")
                lines.append(f"      parent: {GRID_ENTITY_UID}")
        return lines

    def _expand(self, template: str, replacements: dict[str, list[str]], indent: int = 4) -> list[str]:
        """Indent a component template, swapping marker lines for generated blocks.

        Templates are stored unindented so they stay easy to edit; generated
        blocks already carry their own absolute indentation.
        """
        pad = " " * indent
        lines: list[str] = []
        for line in template.rstrip("\n").split("\n"):
            marker = line.strip()
            if marker in replacements:
                lines.extend(replacements[marker])
            elif line.strip():
                lines.append(pad + line)
            else:
                lines.append("")
        return lines

    def render(self) -> str:
        tilemap = self.build_tilemap()

        lines = [
            "meta:",
            f"  format: {MAP_FORMAT}",
            "  category: Map",
            f"  engineVersion: {self.engine_version}",
            '  forkId: ""',
            '  forkVersion: ""',
            f"  time: {datetime.now(timezone.utc).strftime('%m/%d/%Y %H:%M:%S')}",
            f"  entityCount: {self.entity_count}",
            "maps:",
            f"- {MAP_ENTITY_UID}",
            "grids:",
            f"- {GRID_ENTITY_UID}",
            "orphans: []",
            "nullspace: []",
            "tilemap:",
        ]
        for tile_id, tile_number in sorted(tilemap.items(), key=lambda item: item[1]):
            lines.append(f"  {tile_number}: {tile_id}")

        lines.append("entities:")
        lines.append('- proto: ""')
        lines.append("  entities:")
        lines.append(f"  - uid: {MAP_ENTITY_UID}")
        lines.append("    components:")
        lines.extend(self._expand(self.map_entity_template, {}))
        lines.append(f"  - uid: {GRID_ENTITY_UID}")
        lines.append("    components:")
        lines.extend(
            self._expand(
                self.grid_entity_template,
                {
                    "{{MAPGRID_CHUNKS}}": self._render_chunks(tilemap),
                    "{{DECALGRID_NODES}}": self._render_decals(),
                },
            )
        )
        lines.extend(self._render_entities())
        return "\n".join(lines) + "\n"


def decode_chunk(encoded: str) -> list[tuple[int, int, int, int]]:
    """Inverse of the chunk encoding: (typeId, flags, variant, rotationMirroring)."""
    raw = base64.b64decode(encoded)
    if len(raw) % TILE_RECORD.size:
        raise ValueError(f"chunk length {len(raw)} is not a multiple of {TILE_RECORD.size}")
    return [
        TILE_RECORD.unpack_from(raw, offset)
        for offset in range(0, len(raw), TILE_RECORD.size)
    ]
