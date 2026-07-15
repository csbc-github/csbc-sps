#!/usr/bin/env python3
"""
點雲檢視程式：使用 Open3D（OpenGL）呈現，適合大型 PLY／XYZ。
內建 tkinter 控制面板（無需 PyQt6）；命令列可直開檔案。

用法:
  python point_cloud_viewer.py
  python point_cloud_viewer.py C:\\路徑\\檔案.ply
  （有檔名時會直接開 Open3D，不經 tk；若要設定面板請加 --tk）
  預設：子程序 Filament（右下角 XYZ）→ 同程序 Filament → draw → 舊版 Visualizer。
  LJS8_PCV_DRAW_ONLY=1 僅 draw。LJS8_PCV_NO_SUBPROCESS=1 略過子程序。
  子程序若 ACCESS_VIOLATION (3221225477) 會略過同程序 Filament，改走 draw（無 XYZ 列）。

操作（Open3D Filament）: 左鍵旋轉、中鍵平移；右下角 (X,Y,Z) 為英文與數字。
"""
from __future__ import annotations

import argparse
import colorsys
import os
import subprocess
import sys
import time
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from typing import Any, Dict, Optional

import numpy as np

try:
    import open3d as o3d
    import open3d.visualization.gui as gui
    import open3d.visualization.rendering as rendering
except ImportError:
    print("請安裝 Open3D: pip install open3d", file=sys.stderr)
    sys.exit(1)


def _z_to_rgb_colors(z: np.ndarray) -> np.ndarray:
    """以 Z 值對應色相（藍→青→黃→紅），不依賴 matplotlib。"""
    z = np.asarray(z, dtype=np.float64)
    lo, hi = float(np.min(z)), float(np.max(z))
    span = max(hi - lo, 1e-12)
    t = (z - lo) / span
    rgb = np.zeros((z.shape[0], 3), dtype=np.float64)
    for i in range(z.shape[0]):
        h = 0.75 * (1.0 - t[i])
        r, g, b = colorsys.hsv_to_rgb(h, 0.88, 0.96)
        rgb[i] = (r, g, b)
    return rgb


def load_point_cloud(path: str) -> o3d.geometry.PointCloud:
    """以 Open3D 讀取（支援 ASCII／二進位 PLY、XYZ、PCD 等）。"""
    pcd = o3d.io.read_point_cloud(path)
    if not pcd.has_points():
        raise ValueError("無法讀取點雲或檔案為空，請確認格式與路徑。")
    return pcd


def prepare_point_cloud(
    pcd: o3d.geometry.PointCloud,
    *,
    flatten_z: bool,
    color_by_height: bool,
    voxel_size: float,
    max_points: int,
) -> o3d.geometry.PointCloud:
    """複製並套用 Z 去底、體素降採樣、點數上限、著色。"""
    pcd = o3d.geometry.PointCloud(pcd)
    pts = np.asarray(pcd.points, dtype=np.float64)
    if pts.size == 0:
        return pcd

    if flatten_z:
        pts = pts.copy()
        pts[:, 2] -= np.min(pts[:, 2])
        pcd.points = o3d.utility.Vector3dVector(pts)

    if voxel_size > 0:
        pcd = pcd.voxel_down_sample(voxel_size)

    n = len(pcd.points)
    if max_points > 0 and n > max_points:
        ratio = max_points / float(n)
        pcd = pcd.random_down_sample(ratio)

    pts = np.asarray(pcd.points, dtype=np.float64)
    z = pts[:, 2]

    if color_by_height:
        pcd.colors = o3d.utility.Vector3dVector(_z_to_rgb_colors(z))
    elif not pcd.has_colors():
        pcd.colors = o3d.utility.Vector3dVector(_z_to_rgb_colors(z))

    return pcd


def _points_for_picking(pcd: o3d.geometry.PointCloud, max_points: int = 250_000) -> np.ndarray:
    """大點雲時做體素降採樣，供即時射線鄰近查詢（近似螢幕上的點）。"""
    pts = np.asarray(pcd.points, dtype=np.float64)
    n = pts.shape[0]
    if n <= max_points:
        return pts
    tmp = pcd
    extent = float(np.max(pcd.get_max_bound() - pcd.get_min_bound()))
    voxel = max(extent / 256.0, 1e-9)
    for _ in range(24):
        tmp = tmp.voxel_down_sample(voxel)
        if len(tmp.points) <= max_points:
            return np.asarray(tmp.points, dtype=np.float64)
        voxel *= 1.35
    return np.asarray(tmp.points, dtype=np.float64)


def _closest_point_to_ray(
    origin: np.ndarray,
    direction: np.ndarray,
    points: np.ndarray,
) -> tuple[float, np.ndarray]:
    """
    回傳 (距離, 最近點)。direction 需為單位向量。
    距離為點到射線的最短距離；僅考慮射線正方向 (t>=0)。
    """
    if points.size == 0:
        return float("inf"), np.zeros(3)
    d = direction.astype(np.float64)
    nrm = np.linalg.norm(d)
    if nrm < 1e-15:
        return float("inf"), np.zeros(3)
    d = d / nrm
    o = origin.astype(np.float64)
    q = points - o
    t = q @ d
    t = np.maximum(t, 0.0)
    closest_on_ray = o + np.outer(t, d)
    diff = points - closest_on_ray
    d2 = np.einsum("ij,ij->i", diff, diff)
    i = int(np.argmin(d2))
    return float(np.sqrt(d2[i])), points[i]


def _mouse_move_types():
    """不同 Open3D 版本列舉名稱可能不同。"""
    t = gui.MouseEvent.Type
    out = []
    for name in ("MOVE", "MOUSE_MOVE", "DRAG"):
        if hasattr(t, name):
            out.append(getattr(t, name))
    return out


def _event_ignored():
    er = gui.SceneWidget.EventCallbackResult
    return er.IGNORED


def _is_windows_access_violation_exit(code: int) -> bool:
    """subprocess 結束碼 3221225477 / -1073741819 等於 STATUS_ACCESS_VIOLATION (Filament 常見崩潰)。"""
    if code == 3221225477:
        return True
    if code == -1073741819:
        return True
    if code == -1073740791:  # 0xC0000409 stack buffer overrun (少見)
        return True
    return False


def _visualize_with_draw_api(
    pcd: o3d.geometry.PointCloud,
    *,
    window_name: str,
    width: int,
    height: int,
    point_size: float,
    dark_bg: bool,
    show_frame: bool,
) -> bool:
    """
    o3d.visualization.draw：與 Filament Application 不同的事件迴圈，可避免「FEngine 建立後立刻結束」。
    成功則回傳 True。
    """
    draw_fn = getattr(o3d.visualization, "draw", None)
    if draw_fn is None:
        return False
    geoms: list = [pcd]
    if show_frame:
        pts = np.asarray(pcd.points)
        d = float(np.linalg.norm(np.ptp(pts, axis=0))) if len(pts) else 0.0
        s = max(d * 0.08, 1e-3)
        geoms.append(o3d.geometry.TriangleMesh.create_coordinate_frame(size=s))
    bg = (0.12, 0.14, 0.18, 1.0) if dark_bg else (0.96, 0.97, 0.98, 1.0)
    try:
        t0 = time.monotonic()
        draw_fn(
            geoms,
            title=window_name,
            width=width,
            height=height,
            bg_color=bg,
            show_ui=True,
            point_size=float(point_size),
        )
        if time.monotonic() - t0 < 0.08:
            return False
        return True
    except TypeError:
        try:
            t0 = time.monotonic()
            draw_fn(geoms, title=window_name, width=width, height=height)
            if time.monotonic() - t0 < 0.08:
                return False
            return True
        except Exception:
            return False
    except Exception:
        return False


def _run_filament_application_xyz(
    pcd: o3d.geometry.PointCloud,
    *,
    window_name: str,
    width: int,
    height: int,
    point_size: float,
    dark_bg: bool,
    show_frame: bool,
) -> str:
    """
    Filament GUI，右下角 (X,Y,Z)。回傳 'ok' | 'quick' | 'error'。
    """
    pick_pts = _points_for_picking(pcd)
    bbox = pcd.get_axis_aligned_bounding_box()
    mn = np.asarray(bbox.min_bound, dtype=np.float64)
    mx = np.asarray(bbox.max_bound, dtype=np.float64)
    diag = float(np.linalg.norm(mx - mn))
    hit_tol = max(diag * 0.12, 1e-4)

    try:
        app = gui.Application.instance
        app.initialize()
        window = app.create_window(window_name, width, height)
        try:
            window.show(True)
        except Exception:
            pass

        scene_widget = gui.SceneWidget()
        try:
            scene_widget.set_view_controls(gui.SceneWidget.Controls.ROTATE_CAMERA)
        except Exception:
            pass
        scene = rendering.Open3DScene(window.renderer)
        scene_widget.scene = scene

        mat = rendering.MaterialRecord()
        mat.shader = "defaultUnlit"
        mat.point_size = float(point_size)
        scene.add_geometry("cloud", pcd, mat)

        if dark_bg:
            scene.set_background(np.array([0.12, 0.14, 0.18, 1.0], dtype=np.float32), None)
        else:
            scene.set_background(np.array([0.96, 0.97, 0.98, 1.0], dtype=np.float32), None)

        if show_frame:
            scene.show_axes(True)

        scene_widget.setup_camera(60.0, bbox, bbox.get_center())

        coord_label = gui.Label("(X,Y,Z) move mouse over points")
        try:
            coord_label.text_color = (
                gui.Color(0.95, 0.96, 0.98) if dark_bg else gui.Color(0.15, 0.18, 0.22)
            )
        except Exception:
            pass
        try:
            if hasattr(coord_label, "font_em_size"):
                coord_label.font_em_size = 1.15 * window.theme.font_size
        except Exception:
            pass

        window.add_child(scene_widget)
        window.add_child(coord_label)

        def on_window_layout(layout_context):
            cr = window.content_rect
            x0, y0 = int(cr.x), int(cr.y)
            w, h = int(cr.width), int(cr.height)
            if w < 32 or h < 32:
                return
            scene_widget.frame = gui.Rect(x0, y0, w, h)
            try:
                fs = float(window.theme.font_size)
            except Exception:
                fs = 14.0
            margin = int(max(10, round(fs * 0.7)))
            lh = int(max(34, round(fs * 2.2)))
            lw = min(560, max(220, w - 2 * margin))
            coord_label.frame = gui.Rect(x0 + w - lw - margin, y0 + h - lh - margin, lw, lh)

        window.set_on_layout(on_window_layout)
        window.set_needs_layout()

        move_types = _mouse_move_types()
        last_t = [0.0]
        interval = 0.04

        def on_mouse(event):
            if move_types and event.type not in move_types:
                return _event_ignored()
            now = time.monotonic()
            if now - last_t[0] < interval:
                return _event_ignored()
            last_t[0] = now

            cam = scene.camera
            fr = scene_widget.frame
            vw, vh = float(fr.width), float(fr.height)
            if vw < 8 or vh < 8:
                return _event_ignored()

            x, y = float(event.x), float(event.y)
            y_inv = vh - 1.0 - y

            best_d = float("inf")
            best_pt = None
            for yu in (y_inv, y, vh - y):
                try:
                    p0 = np.asarray(cam.unproject(x, yu, 0.0, vw, vh)).reshape(-1)
                    p1 = np.asarray(cam.unproject(x, yu, 1.0, vw, vh)).reshape(-1)
                    ray_dir = p1 - p0
                    dist, hit_pt = _closest_point_to_ray(p0, ray_dir, pick_pts)
                    if dist < best_d:
                        best_d = dist
                        best_pt = hit_pt
                except Exception:
                    continue

            if best_pt is not None and best_d <= hit_tol:
                coord_label.text = (
                    f"(X,Y,Z)=({best_pt[0]:.4f},{best_pt[1]:.4f},{best_pt[2]:.4f})"
                )
            else:
                coord_label.text = "(X,Y,Z)=(?,?,?)"

            return _event_ignored()

        scene_widget.set_on_mouse(on_mouse)
        t_run0 = time.monotonic()
        app.run()
        dt = time.monotonic() - t_run0
        return "ok" if dt >= 0.25 else "quick"
    except Exception as ex:
        import traceback

        print("[point_cloud_viewer] Filament XYZ GUI:", ex, file=sys.stderr)
        traceback.print_exc()
        return "error"


def _spawn_filament_hud_subprocess(
    abs_path: str,
    *,
    window_name: str,
    width: int,
    height: int,
    point_size: float,
    dark_bg: bool,
    show_frame: bool,
    hud_prepare: Dict[str, Any],
) -> int:
    env = os.environ.copy()
    env["LJS8_PCV_WINDOW_NAME"] = window_name
    env["LJS8_PCV_WIDTH"] = str(width)
    env["LJS8_PCV_HEIGHT"] = str(height)
    env["LJS8_PCV_PT_SIZE"] = str(point_size)
    env["LJS8_PCV_DARK"] = "1" if dark_bg else "0"
    env["LJS8_PCV_FRAME"] = "1" if show_frame else "0"
    env["LJS8_PCV_VOXEL"] = str(hud_prepare.get("voxel", 0.0))
    env["LJS8_PCV_MAXPTS"] = str(int(hud_prepare.get("max_points", 2_000_000)))
    env["LJS8_PCV_FLATTEN"] = "1" if hud_prepare.get("flatten_z", True) else "0"
    env["LJS8_PCV_COLOR_Z"] = "1" if hud_prepare.get("color_by_height", False) else "0"
    script = os.path.abspath(__file__)
    try:
        cp = subprocess.run(
            [sys.executable, script, "--internal-filament-hud", os.path.abspath(abs_path)],
            env=env,
        )
        return int(cp.returncode)
    except Exception as e:
        print("[point_cloud_viewer] subprocess HUD:", e, file=sys.stderr)
        return -1


def internal_filament_hud_main(path: str) -> None:
    """子程序專用：載入 path、依環境變數處理後只跑 Filament XYZ。"""
    window_name = os.environ.get("LJS8_PCV_WINDOW_NAME", "Point cloud")
    width = int(os.environ.get("LJS8_PCV_WIDTH", "1400"))
    height = int(os.environ.get("LJS8_PCV_HEIGHT", "900"))
    pt_size = float(os.environ.get("LJS8_PCV_PT_SIZE", "2.5"))
    dark_bg = os.environ.get("LJS8_PCV_DARK", "1") == "1"
    show_frame = os.environ.get("LJS8_PCV_FRAME", "1") == "1"
    voxel = float(os.environ.get("LJS8_PCV_VOXEL", "0"))
    max_pts = int(os.environ.get("LJS8_PCV_MAXPTS", "2000000"))
    flatten_z = os.environ.get("LJS8_PCV_FLATTEN", "1") == "1"
    color_by_height = os.environ.get("LJS8_PCV_COLOR_Z", "0") == "1"

    raw = load_point_cloud(path)
    pcd = prepare_point_cloud(
        raw,
        flatten_z=flatten_z,
        color_by_height=color_by_height,
        voxel_size=voxel,
        max_points=max_pts,
    )
    result = _run_filament_application_xyz(
        pcd,
        window_name=window_name,
        width=width,
        height=height,
        point_size=pt_size,
        dark_bg=dark_bg,
        show_frame=show_frame,
    )
    if result == "ok":
        sys.exit(0)
    if result == "quick":
        sys.exit(2)
    sys.exit(1)


def open3d_visualize(
    pcd: o3d.geometry.PointCloud,
    *,
    window_name: str = "點雲檢視 (Open3D)",
    width: int = 1400,
    height: int = 900,
    point_size: float = 2.5,
    dark_bg: bool = True,
    show_frame: bool = True,
    source_path: Optional[str] = None,
    hud_prepare: Optional[Dict[str, Any]] = None,
) -> None:
    """
    子程序 Filament（右下角 XYZ）→ 同程序 Filament（子程序 ACCESS_VIOLATION 時略過）
    → o3d.visualization.draw → 舊版 Visualizer。

    LJS8_PCV_DRAW_ONLY=1：只要 draw。LJS8_PCV_NO_SUBPROCESS=1：跳過子程序。
    """
    hp: Dict[str, Any] = hud_prepare if hud_prepare is not None else {
        "voxel": 0.0,
        "max_points": 2_000_000,
        "flatten_z": True,
        "color_by_height": False,
    }

    if os.environ.get("LJS8_PCV_DRAW_ONLY", "").strip() in ("1", "true", "yes"):
        if _visualize_with_draw_api(
            pcd,
            window_name=window_name,
            width=width,
            height=height,
            point_size=point_size,
            dark_bg=dark_bg,
            show_frame=show_frame,
        ):
            return
        _legacy_visualizer_run(
            pcd,
            window_name=window_name,
            width=width,
            height=height,
            point_size=point_size,
            dark_bg=dark_bg,
            show_frame=show_frame,
        )
        return

    skip_inproc_filament = os.environ.get("LJS8_PCV_SKIP_FILAMENT", "").strip() in (
        "1",
        "true",
        "yes",
    )

    abs_src = os.path.abspath(source_path) if source_path else ""
    if (
        abs_src
        and os.path.isfile(abs_src)
        and os.environ.get("LJS8_PCV_NO_SUBPROCESS", "").strip() not in ("1", "true", "yes")
    ):
        code = _spawn_filament_hud_subprocess(
            abs_src,
            window_name=window_name,
            width=width,
            height=height,
            point_size=point_size,
            dark_bg=dark_bg,
            show_frame=show_frame,
            hud_prepare=hp,
        )
        if code == 0:
            return
        if _is_windows_access_violation_exit(code):
            print(
                "[point_cloud_viewer] Filament subprocess crashed (ACCESS_VIOLATION). "
                "Skipping in-process Filament.",
                file=sys.stderr,
            )
            skip_inproc_filament = True
        elif code == 2:
            print(
                "[point_cloud_viewer] subprocess HUD exited quickly; trying in-process...",
                file=sys.stderr,
            )
        elif code != 0:
            print(
                f"[point_cloud_viewer] subprocess HUD exit {code}; trying in-process...",
                file=sys.stderr,
            )

    result = "skipped" if skip_inproc_filament else None
    if not skip_inproc_filament:
        result = _run_filament_application_xyz(
            pcd,
            window_name=window_name,
            width=width,
            height=height,
            point_size=point_size,
            dark_bg=dark_bg,
            show_frame=show_frame,
        )
    if result == "ok":
        return
    if result == "quick":
        print(
            "[point_cloud_viewer] in-process Filament ended quickly; trying draw()...",
            file=sys.stderr,
        )

    if _visualize_with_draw_api(
        pcd,
        window_name=window_name,
        width=width,
        height=height,
        point_size=point_size,
        dark_bg=dark_bg,
        show_frame=show_frame,
    ):
        return

    _legacy_visualizer_run(
        pcd,
        window_name=window_name,
        width=width,
        height=height,
        point_size=point_size,
        dark_bg=dark_bg,
        show_frame=show_frame,
    )


def _legacy_visualizer_run(
    pcd: o3d.geometry.PointCloud,
    *,
    window_name: str,
    width: int,
    height: int,
    point_size: float,
    dark_bg: bool,
    show_frame: bool,
) -> None:
    vis = o3d.visualization.Visualizer()
    vis.create_window(window_name=window_name, width=width, height=height, visible=True)
    vis.add_geometry(pcd)
    opt = vis.get_render_option()
    opt.point_size = float(point_size)
    opt.point_show_normal = False
    if dark_bg:
        opt.background_color = np.array([0.12, 0.14, 0.18], dtype=np.float64)
    else:
        opt.background_color = np.array([0.96, 0.97, 0.98], dtype=np.float64)

    if show_frame:
        pts = np.asarray(pcd.points)
        d = float(np.linalg.norm(np.ptp(pts, axis=0))) if len(pts) else 0.0
        s = max(d * 0.08, 1e-3)
        frame = o3d.geometry.TriangleMesh.create_coordinate_frame(size=s)
        vis.add_geometry(frame)

    ctr = vis.get_view_control()
    ctr.set_zoom(0.65)
    vis.run()
    vis.destroy_window()


class ViewerApp:
    def __init__(self, initial_path: Optional[str] = None):
        self.root = tk.Tk()
        self.root.title("點雲檢視 — Open3D")
        self.root.minsize(420, 380)
        self._path_var = tk.StringVar(value=initial_path or "")
        self._build()

    def _build(self) -> None:
        pad = {"padx": 8, "pady": 6}
        f_top = ttk.Frame(self.root)
        f_top.pack(fill=tk.X, **pad)
        ttk.Button(f_top, text="開啟檔案…", command=self._browse).pack(side=tk.LEFT)
        ttk.Label(f_top, textvariable=self._path_var, wraplength=360).pack(
            side=tk.LEFT, fill=tk.X, expand=True, padx=8
        )

        f_opt = ttk.LabelFrame(self.root, text="顯示選項")
        f_opt.pack(fill=tk.X, **pad)

        self._flat = tk.BooleanVar(value=True)
        ttk.Checkbutton(f_opt, text="Z 去底（最低點為 0）", variable=self._flat).grid(
            row=0, column=0, columnspan=2, sticky=tk.W, padx=8, pady=4
        )

        self._color_z = tk.BooleanVar(value=False)
        ttk.Checkbutton(
            f_opt,
            text="強制依高度著色（覆蓋檔案原有顏色／強度色）",
            variable=self._color_z,
        ).grid(row=1, column=0, columnspan=2, sticky=tk.W, padx=8, pady=4)

        ttk.Label(f_opt, text="點大小:").grid(row=2, column=0, sticky=tk.W, padx=8)
        self._pt_size = tk.DoubleVar(value=2.5)
        ttk.Scale(
            f_opt,
            from_=0.5,
            to=8.0,
            variable=self._pt_size,
            orient=tk.HORIZONTAL,
            length=220,
        ).grid(row=2, column=1, sticky=tk.EW, padx=8, pady=4)

        ttk.Label(f_opt, text="體素降採樣 (0=關閉，單位與座標相同):").grid(
            row=3, column=0, sticky=tk.W, padx=8
        )
        self._voxel = tk.StringVar(value="0")
        ttk.Entry(f_opt, textvariable=self._voxel, width=12).grid(
            row=3, column=1, sticky=tk.W, padx=8, pady=4
        )

        ttk.Label(f_opt, text="最多點數 (0=不限制，大檔建議 2e6 以下):").grid(
            row=4, column=0, sticky=tk.W, padx=8
        )
        self._max_pts = tk.StringVar(value="2000000")
        ttk.Entry(f_opt, textvariable=self._max_pts, width=12).grid(
            row=4, column=1, sticky=tk.W, padx=8, pady=4
        )

        self._dark = tk.BooleanVar(value=True)
        ttk.Checkbutton(f_opt, text="深色背景", variable=self._dark).grid(
            row=5, column=0, columnspan=2, sticky=tk.W, padx=8, pady=4
        )

        self._frame = tk.BooleanVar(value=True)
        ttk.Checkbutton(f_opt, text="顯示座標軸", variable=self._frame).grid(
            row=6, column=0, columnspan=2, sticky=tk.W, padx=8, pady=4
        )

        f_opt.columnconfigure(1, weight=1)

        self._stats = tk.StringVar(value="尚未載入")
        ttk.Label(self.root, textvariable=self._stats, justify=tk.LEFT).pack(
            anchor=tk.W, padx=12, pady=4
        )

        f_btn = ttk.Frame(self.root)
        f_btn.pack(fill=tk.X, **pad)
        ttk.Button(f_btn, text="在 Open3D 視窗中開啟", command=self._show).pack(
            side=tk.LEFT, padx=4
        )
        ttk.Button(f_btn, text="結束", command=self.root.quit).pack(side=tk.RIGHT, padx=4)

        ttk.Label(
            self.root,
            text="提示：Open3D Filament 視窗右下角為 (X,Y,Z)；請把游標移到點雲上。",
            foreground="#555",
        ).pack(anchor=tk.W, padx=12, pady=8)

    def _browse(self) -> None:
        p = filedialog.askopenfilename(
            title="選擇點雲",
            filetypes=[
                ("點雲", "*.ply *.pcd *.xyz *.pts"),
                ("PLY", "*.ply"),
                ("All", "*.*"),
            ],
        )
        if p:
            self._path_var.set(p)

    def _parse_float(self, s: str, default: float = 0.0) -> float:
        try:
            return float(s.strip())
        except Exception:
            return default

    def _parse_int(self, s: str, default: int = 0) -> int:
        try:
            return int(float(s.strip()))
        except Exception:
            return default

    def _show(self) -> None:
        path = self._path_var.get().strip()
        if not path:
            messagebox.showinfo("提示", "請先選擇點雲檔案。")
            return
        try:
            raw = load_point_cloud(path)
            n0 = len(raw.points)
        except Exception as e:
            messagebox.showerror("載入失敗", str(e))
            return

        voxel = max(0.0, self._parse_float(self._voxel.get(), 0.0))
        max_pts = max(0, self._parse_int(self._max_pts.get(), 0))

        try:
            pcd = prepare_point_cloud(
                raw,
                flatten_z=self._flat.get(),
                color_by_height=self._color_z.get(),
                voxel_size=voxel,
                max_points=max_pts,
            )
            n1 = len(pcd.points)
        except Exception as e:
            messagebox.showerror("處理失敗", str(e))
            return

        pts = np.asarray(pcd.points)
        self._stats.set(
            f"檔案: {path}\n"
            f"原始點數: {n0:,}  →  顯示點數: {n1:,}\n"
            f"X [{pts[:, 0].min():.4f}, {pts[:, 0].max():.4f}]  "
            f"Y [{pts[:, 1].min():.4f}, {pts[:, 1].max():.4f}]  "
            f"Z [{pts[:, 2].min():.4f}, {pts[:, 2].max():.4f}]"
        )
        self.root.update_idletasks()

        open3d_visualize(
            pcd,
            window_name=f"點雲 — {path}",
            point_size=self._pt_size.get(),
            dark_bg=self._dark.get(),
            show_frame=self._frame.get(),
            source_path=path,
            hud_prepare={
                "voxel": voxel,
                "max_points": max_pts,
                "flatten_z": self._flat.get(),
                "color_by_height": self._color_z.get(),
            },
        )

    def run(self) -> None:
        self.root.mainloop()


def main(argv: Optional[list] = None) -> None:
    argv_list = list(argv if argv is not None else sys.argv[1:])
    if len(argv_list) >= 2 and argv_list[0] == "--internal-filament-hud":
        internal_filament_hud_main(argv_list[1])
        return

    p = argparse.ArgumentParser(description="點雲檢視 (Open3D)")
    p.add_argument("path", nargs="?", help="點雲檔路徑；指定時直接開 3D（不開 tk，避免與 Open3D 事件迴圈衝突）")
    p.add_argument(
        "--no-gui",
        action="store_true",
        help="同「有指定 path」：僅開 3D（可與 path 併用以明確指定）",
    )
    p.add_argument(
        "--tk",
        action="store_true",
        help="即使命令列有 path 仍先開 tk 設定面板（路徑會預填，請手動按鈕開 3D）",
    )
    p.add_argument("--voxel", type=float, default=0.0, help="體素大小，0 表示關閉")
    p.add_argument(
        "--max-points",
        type=int,
        default=2_000_000,
        help="最多顯示點數；0 表示不限制（大檔可能較慢）",
    )
    p.add_argument("--point-size", type=float, default=2.5)
    args = p.parse_args(argv_list)

    def _run_open3d_direct(file_path: str) -> None:
        raw = load_point_cloud(file_path)
        pcd = prepare_point_cloud(
            raw,
            flatten_z=True,
            color_by_height=False,
            voxel_size=args.voxel,
            max_points=args.max_points,
        )
        open3d_visualize(
            pcd,
            point_size=args.point_size,
            source_path=file_path,
            hud_prepare={
                "voxel": args.voxel,
                "max_points": args.max_points,
                "flatten_z": True,
                "color_by_height": False,
            },
        )

    # 命令列帶檔案時預設「只開 Open3D」，勿先開 tk 再用 after() 叫 Open3D（部分環境會 FEngine 建立後立刻結束）
    if args.path and not args.tk:
        _run_open3d_direct(args.path)
        return

    if args.no_gui and not args.path:
        p.error("使用 --no-gui 時請指定點雲檔 path")

    app = ViewerApp(initial_path=args.path or None)
    app.run()


if __name__ == "__main__":
    main()
