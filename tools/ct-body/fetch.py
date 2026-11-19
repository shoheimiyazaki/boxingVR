"""Fetch TotalSegmentator v2.0.1 subjects (CT + 117 masks) without downloading the whole archive.

Data: TotalSegmentator dataset v2.0.1, J. Wasserthal (University Hospital Basel), CC BY 4.0.
  small subset (102 subjects, 3.2 GB): https://zenodo.org/records/10047263  (DOI 10.5281/zenodo.10047263)
  full set   (1228 subjects, 23.6 GB): https://zenodo.org/records/10047292  (DOI 10.5281/zenodo.10047292)
  paper to cite: https://doi.org/10.1148/ryai.230024

Files land OUTSIDE the repository (default ../ct-data/<set> next to the repo; override with --cache or CTBODY_CACHE).

Usage:
  py -3 -I tools/ct-body/fetch.py meta                       # meta.csv only
  py -3 -I tools/ct-body/fetch.py survey                     # head-to-foot coverage of every candidate (header + mask sizes only)
  py -3 -I tools/ct-body/fetch.py subject s1029              # one subject's ct.nii.gz + segmentations/
"""
from __future__ import annotations

import argparse
import csv
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import nifti  # noqa: E402
from remote_zip import open_remote_zip  # noqa: E402

SETS = {
    "small": "https://zenodo.org/records/10047263/files/Totalsegmentator_dataset_small_v201.zip?download=1",
    "full": "https://zenodo.org/records/10047292/files/Totalsegmentator_dataset_v201.zip?download=1",
}

REPO = Path(__file__).resolve().parents[2]

# Labels whose presence tells how much of the body a scan covers (top of head .. thigh .. arms).
COVERAGE_LABELS = ["brain", "skull", "vertebrae_C1", "heart", "hip_left", "femur_left", "femur_right", "humerus_left", "humerus_right"]

# A study type is a whole-body candidate if it contains one of these (meta.csv `study_type`).
WHOLE_BODY_STUDIES = ("whole body", "polytrauma", "neck-thorax-abdomen-pelvis")


def cache_dir(args) -> Path:
    root = Path(args.cache or os.environ.get("CTBODY_CACHE") or REPO.parent / "ct-data")
    path = root / f"totalseg-{args.set}"
    path.mkdir(parents=True, exist_ok=True)
    return path


def read_meta(cache: Path, archive) -> list[dict]:
    meta = cache / "meta.csv"
    if not meta.exists():
        meta.write_bytes(archive.read("meta.csv"))
    with meta.open(encoding="utf-8-sig") as f:
        return list(csv.DictReader(f, delimiter=";"))


def survey(archive, rows: list[dict]) -> list[dict]:
    """Coverage of each candidate: scan length along the body (mm) and compressed size of key masks (an empty mask is ~0 kB)."""
    sizes = {i.filename: i.compress_size for i in archive.infolist()}
    out = []
    for row in rows:
        if not any(k in row["study_type"] for k in WHOLE_BODY_STUDIES):
            continue
        sid = row["image_id"]
        with archive.open(f"{sid}/ct.nii.gz") as f:
            header = nifti.read_header_gz(f)
        length = header.shape[2] * header.zooms[2]
        masks = {label: sizes.get(f"{sid}/segmentations/{label}.nii.gz", 0) // 1000 for label in COVERAGE_LABELS}
        out.append({"id": sid, "study": row["study_type"], "sex": row["gender"], "age": row["age"], "length_mm": round(length), **masks})
    return sorted(out, key=lambda r: -r["length_mm"])


def fetch_subject(archive, sid: str, cache: Path) -> Path:
    target = cache / sid
    members = [n for n in archive.namelist() if n.startswith(f"{sid}/") and n.endswith(".nii.gz")]
    if not members:
        raise SystemExit(f"subject {sid} is not in the archive")
    for name in members:
        out = cache / name
        if out.exists() and out.stat().st_size == archive.getinfo(name).file_size:
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        tmp = out.with_suffix(".part")
        tmp.write_bytes(archive.read(name))  # zipfile checks the CRC
        tmp.replace(out)
    print(f"{sid}: {len(members)} files in {target}")
    return target


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["meta", "survey", "subject"])
    parser.add_argument("ids", nargs="*")
    parser.add_argument("--set", choices=sorted(SETS), default="small")
    parser.add_argument("--cache")
    args = parser.parse_args()

    cache = cache_dir(args)
    archive = open_remote_zip(SETS[args.set])
    rows = read_meta(cache, archive)
    if args.command == "meta":
        print(f"{len(rows)} subjects; meta.csv in {cache}")
    elif args.command == "survey":
        result = survey(archive, rows)
        keys = list(result[0].keys()) if result else []
        print("\t".join(keys))
        for r in result:
            print("\t".join(str(r[k]) for k in keys))
    else:
        if not args.ids:
            raise SystemExit("give one or more subject ids, e.g. s1029")
        for sid in args.ids:
            fetch_subject(archive, sid, cache)


if __name__ == "__main__":
    main()
