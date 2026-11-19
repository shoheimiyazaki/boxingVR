"""TotalSegmentator v2 (117 labels) -> game material classes.

Tool classes (CLS_*) are what the converter votes on per game cell; `GAME_MATERIAL` turns them into the
BoxerAnatomy material numbers written to the .vox. FAT and the unlabeled soft tissue are separate classes so
the plan can give fat its own material later (today both become muscle, docs/DEBT.md).

The part (rigid body) a voxel belongs to is not decided here: it comes from where the voxel lies relative to
the landmarks (build.py), because most labels (skin, fat, muscle) cross part borders.
"""
from __future__ import annotations

# Tool classes. 0 = outside the body.
CLS_EMPTY = 0
CLS_SOFT = 1      # unlabeled soft tissue inside the body outline (HU -30..), muscle-like
CLS_FAT = 2       # unlabeled fat (HU -190..-30): subcutaneous and visceral fat
CLS_MUSCLE = 3    # labeled skeletal muscle (gluteus, autochthon, iliopsoas)
CLS_BONE = 4      # labeled bone + unlabeled HU > BONE_HU inside the body (forearm, mandible remnants, ...)
CLS_BRAIN = 5
CLS_CORD = 6
CLS_HEART = 7
CLS_LUNG = 8
CLS_LIVER = 9
CLS_GUT = 10      # digestive tract + other abdominal/pelvic organs (no own game material yet)
CLS_VESSEL = 11   # great veins: no game material yet, treated as soft tissue
CLS_AIRWAY = 12   # trachea and main bronchi: part of the lung (game material lung, user's request 2026-11-19)
CLS_ARTERY = 13   # aorta and its branches (labeled, plus contrast-filled continuations traced in build.py)
COUNT = 14

NAMES = ["empty", "soft", "fat", "muscle", "bone", "brain", "cord", "heart", "lung", "liver", "gut", "vein", "airway", "artery"]

# BoxerAnatomy material numbers (games/MiniRing/Voxels/BoxerAnatomy.cs).
SKIN, MUSCLE, BONE, BRAIN, HEART, LUNG, LIVER, GUT, EYE = 1, 2, 3, 4, 5, 6, 7, 8, 9
CORD = 22
ARTERY = 26

GAME_MATERIAL = {
    CLS_EMPTY: 0, CLS_SOFT: MUSCLE, CLS_FAT: MUSCLE, CLS_MUSCLE: MUSCLE, CLS_BONE: BONE, CLS_BRAIN: BRAIN, CLS_CORD: CORD,
    CLS_HEART: HEART, CLS_LUNG: LUNG, CLS_LIVER: LIVER, CLS_GUT: GUT, CLS_VESSEL: MUSCLE, CLS_AIRWAY: LUNG, CLS_ARTERY: ARTERY,
}

# HU windows for voxels no mask covers (inside the body outline only).
BODY_HU = -500     # air/lung below this; the outline is the largest connected component above it (drops the table)
FAT_HU = (-190, -30)
BONE_HU = 250      # cortical/trabecular bone; contrast-filled vessels and kidneys are labeled, so masks override this

_BONE_PREFIXES = ("vertebrae_", "rib_")
_BONES = {"skull", "sternum", "costal_cartilages", "sacrum", "clavicula_left", "clavicula_right", "scapula_left", "scapula_right",
          "humerus_left", "humerus_right", "hip_left", "hip_right", "femur_left", "femur_right"}
_MUSCLES = {"gluteus_maximus", "gluteus_medius", "gluteus_minimus", "autochthon", "iliopsoas"}
_HEART = {"heart", "atrial_appendage_left"}
_LIVER = {"liver", "gallbladder"}
_GUT = {"esophagus", "stomach", "duodenum", "small_bowel", "colon", "spleen", "pancreas", "kidney_left", "kidney_right",
        "kidney_cyst_left", "kidney_cyst_right", "adrenal_gland_left", "adrenal_gland_right", "urinary_bladder", "prostate"}
ARTERIES = {"aorta", "brachiocephalic_trunk", "subclavian_artery_left", "subclavian_artery_right", "common_carotid_artery_left",
            "common_carotid_artery_right", "iliac_artery_left", "iliac_artery_right"}
_VESSELS = {"pulmonary_vein", "brachiocephalic_vein_left", "brachiocephalic_vein_right", "superior_vena_cava", "inferior_vena_cava",
            "portal_vein_and_splenic_vein", "iliac_vena_left", "iliac_vena_right"}


def _strip_side(name: str) -> str:
    for suffix in ("_left", "_right"):
        if name.endswith(suffix):
            return name[: -len(suffix)]
    return name


def tool_class(label: str) -> int:
    """Tool class of one TotalSegmentator label file name (without .nii.gz). Unknown labels raise: the table must be complete."""
    if label in _BONES or label.startswith(_BONE_PREFIXES):
        return CLS_BONE
    if label == "brain":
        return CLS_BRAIN
    if label == "spinal_cord":
        return CLS_CORD
    if label in _HEART:
        return CLS_HEART
    if label.startswith("lung_"):
        return CLS_LUNG
    if label in _LIVER:
        return CLS_LIVER
    if label in _GUT:
        return CLS_GUT
    if label in ARTERIES:
        return CLS_ARTERY
    if label in _VESSELS:
        return CLS_VESSEL
    if label == "trachea":
        return CLS_AIRWAY
    if _strip_side(label) in _MUSCLES:
        return CLS_MUSCLE
    if label == "thyroid_gland":
        return CLS_SOFT
    raise KeyError(f"no game class for TotalSegmentator label '{label}'; add it to tools/ct-body/labels.py")


# Order in which overlapping masks are painted (later wins). Masks barely overlap; organs over vessels over bone keeps
# contrast-filled organs from being read as bone and the cord inside the vertebrae.
PAINT_ORDER = [CLS_SOFT, CLS_MUSCLE, CLS_VESSEL, CLS_AIRWAY, CLS_GUT, CLS_LIVER, CLS_LUNG, CLS_HEART, CLS_ARTERY, CLS_BONE, CLS_BRAIN, CLS_CORD]
