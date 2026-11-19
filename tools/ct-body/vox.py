"""MagicaVoxel .vox read/write in the same layout as games/MiniRing/Voxels/MagicaVox.cs (one named model per body part).

Axes follow BoxerVoxelModel: game part-local (x, y, z) with Y up and +Z the body's front is stored in .vox as
(x, Dz - 1 - z, y) (the .vox Z is up, the body faces .vox -Y). Colour index = material number.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass

import numpy as np


@dataclass(frozen=True)
class VoxModel:
    name: str
    cells: np.ndarray  # uint8, indexed [x, y, z] in .vox axes


def _dict(items: list[tuple[str, str]]) -> bytes:
    out = struct.pack("<i", len(items))
    for k, v in items:
        kb, vb = k.encode(), v.encode()
        out += struct.pack("<i", len(kb)) + kb + struct.pack("<i", len(vb)) + vb
    return out


def _chunk(tag: str, content: bytes) -> bytes:
    return tag.encode() + struct.pack("<ii", len(content), 0) + content


def write(path, models: list[VoxModel], palette: list[int]) -> None:
    if len(palette) != 256:
        raise ValueError("palette needs 256 entries")
    body = b""
    for m in models:
        sx, sy, sz = m.cells.shape
        if max(sx, sy, sz) > 256:
            raise ValueError(f"model {m.name} is larger than 256 cells on a side: {m.cells.shape}")
        xs, ys, zs = np.nonzero(m.cells)
        xyzi = np.stack([xs, ys, zs, m.cells[xs, ys, zs]], axis=1).astype(np.uint8)
        body += _chunk("SIZE", struct.pack("<iii", sx, sy, sz))
        body += _chunk("XYZI", struct.pack("<i", len(xyzi)) + xyzi.tobytes())
    body += _chunk("nTRN", struct.pack("<i", 0) + _dict([]) + struct.pack("<iii", 1, -1, 0) + struct.pack("<i", 1) + _dict([("_t", "0 0 0")]))
    body += _chunk("nGRP", struct.pack("<i", 1) + _dict([]) + struct.pack("<i", len(models)) + b"".join(struct.pack("<i", 2 + 2 * i) for i in range(len(models))))
    x0 = 0
    for i, m in enumerate(models):
        sx, sy, sz = m.cells.shape
        at = f"{x0 + sx // 2} 0 {sz // 2}"
        x0 += sx + 4
        body += _chunk("nTRN", struct.pack("<i", 2 + 2 * i) + _dict([("_name", m.name)]) + struct.pack("<iii", 3 + 2 * i, -1, 0) + struct.pack("<i", 1) + _dict([("_t", at)]))
        body += _chunk("nSHP", struct.pack("<i", 3 + 2 * i) + _dict([]) + struct.pack("<i", 1) + struct.pack("<i", i) + _dict([]))
    body += _chunk("LAYR", struct.pack("<i", 0) + _dict([]) + struct.pack("<i", -1))
    body += _chunk("RGBA", b"".join(struct.pack("<I", palette[i % 256]) for i in range(1, 257)))
    with open(path, "wb") as f:
        f.write(b"VOX " + struct.pack("<i", 200) + b"MAIN" + struct.pack("<ii", 0, len(body)) + body)


def read(path) -> tuple[list[VoxModel], list[int]]:
    """Read named models (nTRN _name) and the palette. Rotations are not applied (the game rejects rotated models)."""
    data = open(path, "rb").read()
    if data[:4] != b"VOX ":
        raise ValueError("not a .vox file")
    pos = 20  # header(8) + MAIN id/sizes(12)
    sizes, grids, names, shapes, palette = [], [], {}, {}, None

    def read_dict(buf, p):
        n = struct.unpack_from("<i", buf, p)[0]
        p += 4
        out = {}
        for _ in range(n):
            kl = struct.unpack_from("<i", buf, p)[0]; p += 4
            k = buf[p:p + kl].decode(); p += kl
            vl = struct.unpack_from("<i", buf, p)[0]; p += 4
            out[k] = buf[p:p + vl].decode(); p += vl
        return out, p

    transforms = []
    while pos < len(data):
        tag = data[pos:pos + 4].decode()
        n, _ = struct.unpack_from("<ii", data, pos + 4)
        c = data[pos + 12:pos + 12 + n]
        pos += 12 + n
        if tag == "SIZE":
            sizes.append(struct.unpack_from("<iii", c))
        elif tag == "XYZI":
            count = struct.unpack_from("<i", c)[0]
            v = np.frombuffer(c, np.uint8, count * 4, 4).reshape(-1, 4)
            g = np.zeros(sizes[-1], np.uint8)
            g[v[:, 0], v[:, 1], v[:, 2]] = v[:, 3]
            grids.append(g)
        elif tag == "nTRN":
            node = struct.unpack_from("<i", c)[0]
            attrs, p = read_dict(c, 4)
            child = struct.unpack_from("<i", c, p)[0]
            transforms.append((attrs.get("_name"), child))
        elif tag == "nSHP":
            node = struct.unpack_from("<i", c)[0]
            _, p = read_dict(c, 4)
            p += 4
            shapes[node] = struct.unpack_from("<i", c, p)[0]
        elif tag == "RGBA":
            raw = struct.unpack_from("<256I", c)
            palette = [0] + list(raw[:255])
    for name, child in transforms:
        if child in shapes and name:
            names[shapes[child]] = name
    models = [VoxModel(names.get(i, f"model{i}"), g) for i, g in enumerate(grids)]
    return models, palette or [0] * 256


def to_vox_axes(game: np.ndarray) -> np.ndarray:
    """Game part-local [x, y, z] (Y up, +Z front) -> .vox [x, y, z] (Z up, front = -Y)."""
    return np.flip(np.transpose(game, (0, 2, 1)), axis=1)


def from_vox_axes(cells: np.ndarray) -> np.ndarray:
    return np.transpose(np.flip(cells, axis=1), (0, 2, 1))
