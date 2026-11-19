"""Minimal NIfTI-1 reader (numpy only). Enough for TotalSegmentator's ct.nii.gz and segmentation masks.

Returns the voxel array in its stored (i, j, k) order plus the 4x4 affine (sform if set, else qform), so the
caller can find which array axis points to the patient's head / left / front.
"""
from __future__ import annotations

import gzip
import struct
from dataclasses import dataclass

import numpy as np

_DTYPES = {2: np.uint8, 4: np.int16, 8: np.int32, 16: np.float32, 64: np.float64, 256: np.int8, 512: np.uint16, 768: np.uint32}


@dataclass(frozen=True)
class NiftiHeader:
    shape: tuple[int, ...]
    dtype: np.dtype
    vox_offset: int
    scl_slope: float
    scl_inter: float
    affine: np.ndarray  # 4x4, voxel index -> RAS+ millimetres (NIfTI convention)
    zooms: tuple[float, float, float]
    endian: str


def _quaternion_affine(b, c, d, qfac, pix, offs) -> np.ndarray:
    a = np.sqrt(max(0.0, 1.0 - (b * b + c * c + d * d)))
    r = np.array([
        [a * a + b * b - c * c - d * d, 2 * (b * c - a * d), 2 * (b * d + a * c)],
        [2 * (b * c + a * d), a * a + c * c - b * b - d * d, 2 * (c * d - a * b)],
        [2 * (b * d - a * c), 2 * (c * d + a * b), a * a + d * d - b * b - c * c],
    ])
    scale = np.array([pix[0], pix[1], pix[2] * (qfac if qfac != 0 else 1.0)])
    m = np.eye(4)
    m[:3, :3] = r * scale
    m[:3, 3] = offs
    return m


def parse_header(raw: bytes) -> NiftiHeader:
    if len(raw) < 348:
        raise ValueError("NIfTI header is shorter than 348 bytes")
    endian = "<" if struct.unpack("<i", raw[0:4])[0] == 348 else ">"
    if struct.unpack(endian + "i", raw[0:4])[0] != 348:
        raise ValueError("not a NIfTI-1 file (sizeof_hdr != 348)")
    if raw[344:347] not in (b"n+1", b"ni1"):
        raise ValueError("missing NIfTI-1 magic")
    dim = struct.unpack(endian + "8h", raw[40:56])
    datatype = struct.unpack(endian + "h", raw[70:72])[0]
    if datatype not in _DTYPES:
        raise ValueError(f"unsupported NIfTI datatype {datatype}")
    pixdim = struct.unpack(endian + "8f", raw[76:108])
    vox_offset = int(struct.unpack(endian + "f", raw[108:112])[0])
    scl_slope, scl_inter = struct.unpack(endian + "2f", raw[112:120])
    qform_code, sform_code = struct.unpack(endian + "2h", raw[252:256])
    if sform_code > 0:
        rows = struct.unpack(endian + "12f", raw[280:328])
        affine = np.vstack([np.array(rows).reshape(3, 4), [0, 0, 0, 1]])
    else:
        b, c, d, qx, qy, qz = struct.unpack(endian + "6f", raw[256:280])
        affine = _quaternion_affine(b, c, d, pixdim[0], pixdim[1:4], (qx, qy, qz))
    ndim = dim[0]
    shape = tuple(int(v) for v in dim[1:1 + ndim])
    return NiftiHeader(shape, np.dtype(_DTYPES[datatype]).newbyteorder(endian), max(vox_offset, 352),
                       float(scl_slope), float(scl_inter), affine, tuple(float(p) for p in pixdim[1:4]), endian)


def read_header_gz(data_stream) -> NiftiHeader:
    with gzip.GzipFile(fileobj=data_stream) as gz:
        return parse_header(gz.read(348))


def load(path) -> tuple[np.ndarray, NiftiHeader]:
    """Load a .nii.gz into (array in Fortran (i, j, k) indexing, header). Intensity scaling is applied for non-integer slopes."""
    with gzip.open(path, "rb") as gz:
        raw = gz.read()
    header = parse_header(raw[:348])
    count = int(np.prod(header.shape))
    data = np.frombuffer(raw, dtype=header.dtype, count=count, offset=header.vox_offset)
    array = data.reshape(header.shape, order="F")
    # The spec: a zero (or non-finite) slope means no scaling.
    if np.isfinite(header.scl_slope) and header.scl_slope != 0.0 and (header.scl_slope != 1.0 or header.scl_inter != 0.0):
        array = array.astype(np.float32) * header.scl_slope + header.scl_inter
    return array, header
