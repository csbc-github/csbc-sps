"""Dimensions of the visible reconstructed surface, in calibrated scan axes.

由 Hank IP_control/sick_dimensions.py 搬入，邏輯不改。
"""
import math
from pathlib import Path


class Bounds:
    def __init__(self):
        self.minimum = [math.inf] * 3
        self.maximum = [-math.inf] * 3
        self.count = 0

    def add(self, point):
        if len(point) != 3 or not all(math.isfinite(v) for v in point):
            raise ValueError("點座標必須包含三個有限數值")
        for axis, value in enumerate(point):
            self.minimum[axis] = min(self.minimum[axis], float(value))
            self.maximum[axis] = max(self.maximum[axis], float(value))
        self.count += 1

    def report(self):
        if not self.count:
            raise ValueError("沒有可計算尺寸的點")
        spans = [hi - lo for lo, hi in zip(self.minimum, self.maximum)]
        return {"units": "mm", "method": "visible_surface_axis_aligned_bounds",
                "points": self.count, "length_mm": spans[1], "width_mm": spans[0],
                "height_mm": spans[2], "top_above_baseline_mm": self.maximum[2],
                "min_xyz_mm": self.minimum.copy(), "max_xyz_mm": self.maximum.copy(),
                "length_estimated": True,
                "note": "可見點雲範圍：長=Y跨度（行程與時間估算）、寬=X跨度、高=Z跨度。"
                        "裁切、缺面及離群點會影響結果；不是角鋼腿長或厚度。"}


def measure_ply(source):
    try:
        from plyfile import PlyData
    except ImportError as exc:
        raise RuntimeError("請先執行 python -m pip install plyfile") from exc
    source = Path(source).resolve()
    ply = PlyData.read(str(source), mmap=False)
    comments = list(ply.comments)
    for element in ply.elements:
        comments.extend(element.comments)
    if "geometry 3D_manual_translation_estimated" not in comments:
        raise ValueError("請選本程式重建的 3D PLY，以確認 mm 單位及長寬高座標軸；原始 2D PLY 請先重建")
    if "recording_complete 1" not in comments:
        raise ValueError("PLY 尚未完成")
    if "vertex" not in ply or not {"x", "y", "z"}.issubset(ply["vertex"].data.dtype.names):
        raise ValueError("PLY 缺少 XYZ 座標")
    bounds = Bounds()
    for point in ply["vertex"].data:
        bounds.add(tuple(float(point[axis]) for axis in ("x", "y", "z")))
    return dict(bounds.report(), source=str(source))


def dimensions_text(report):
    return ("可見範圍：長(Y，估算) {length_mm:.2f} × 寬(X) {width_mm:.2f} × 高(Z跨度) {height_mm:.2f} mm"
            "｜最高點距桌面 {top_above_baseline_mm:.2f} mm").format(**report)


def measure_parts(source, gap_mm=10.0, align_left=False):
    """API dimensions: width = X span; height = max Z above calibrated table."""
    if not math.isfinite(gap_mm) or gap_mm <= 0:
        raise ValueError("分件空隙 gap_mm 必須大於 0")
    try:
        import numpy as np
        from plyfile import PlyData
    except ImportError as exc:
        raise RuntimeError("請先執行 python -m pip install plyfile") from exc
    ply = PlyData.read(str(source), mmap=False)
    comments = list(ply.comments)
    for element in ply.elements:
        comments.extend(element.comments)
    if "geometry 3D_manual_translation_estimated" not in comments or "recording_complete 1" not in comments:
        raise ValueError("請選擇本程式已完成的重建 3D PLY；原始 2D PLY 請先重建")
    if "vertex" not in ply or not {"x", "y", "z", "frame_id"}.issubset(ply["vertex"].data.dtype.names):
        raise ValueError("PLY 缺少 XYZ 或 frame_id")
    data = ply["vertex"].data
    points = np.column_stack([data[k] for k in ("x", "y", "z")]).astype(float)
    if not len(points) or not np.isfinite(points).all():
        raise ValueError("PLY 沒有有效點，或包含非有限座標")
    alignment = None
    if align_left:
        from sick_left_align import align_left_points
        points, alignment = align_left_points(points, data["frame_id"], gap_mm)
    order = np.argsort(points[:, 0])
    cuts = np.flatnonzero(np.diff(points[order, 0]) > gap_mm) + 1
    parts = []
    for number, indices in enumerate(np.split(order, cuts), 1):
        selected = points[indices]
        lo, hi = selected.min(axis=0), selected.max(axis=0)
        parts.append({"part_id": number, "point_count": len(indices),
                      "scan_count": len(np.unique(data["frame_id"][indices])),
                      "length_mm": float(hi[1] - lo[1]), "width_mm": float(hi[0] - lo[0]),
                      "height_mm": float(hi[2]), "height_span_mm": float(hi[2] - lo[2]),
                      "min_xyz_mm": lo.tolist(), "max_xyz_mm": hi.tolist(),
                      "length_estimated": True})
    return {"units": "mm", "point_count": len(points), "part_count": len(parts), "parts": parts,
            "measurement_source": "scan_points_without_added_thickness",
            "definitions": {"length_mm": "max(Y)-min(Y); estimated from travel and time",
                            "width_mm": "max(X)-min(X) over all points in each part",
                            "height_mm": "max(Z); highest point above calibrated table",
                            "height_span_mm": "max(Z)-min(Z)"},
            "segmentation": {"method": "separate_nonoverlapping_X_bands", "gap_mm": gap_mm},
            "align_left_requested": align_left,
            "source_left_aligned": any(c.startswith("left_alignment ") for c in comments),
            "alignment": alignment,
            "note": "依 X 空隙由左至右分件；X 投影重疊時請先分別框選。尺寸受缺點、裁切與離群點影響，長度為估算。"}
