"""線掃描子程序：校正空滾輪、錄製 SICK 截面、算出頭尾角點。"""
import argparse
import json
import os
import sys
import time

from hank_line_measure import measure_recorded_frames
from line_scan_geometry import fit_roller_line


def main(argv=None):
    parser = argparse.ArgumentParser(description="SICK 線掃描工作程序")
    parser.add_argument("--ip-control", required=True, help="IP_control 資料夾，用來載入 sick_lidar")
    sub = parser.add_subparsers(dest="command", required=True)

    calibrate = sub.add_parser("calibrate")
    calibrate.add_argument("--ip", required=True)
    calibrate.add_argument("--port", type=int, required=True)
    calibrate.add_argument("--frames", type=int, default=40)
    calibrate.add_argument("--tolerance", type=float, required=True)
    calibrate.add_argument("--range-deviation", type=float, required=True)
    calibrate.add_argument("--out", required=True)

    record = sub.add_parser("record")
    record.add_argument("--ip", required=True)
    record.add_argument("--port", type=int, required=True)
    record.add_argument("--session", required=True)
    record.add_argument("--ready-timeout", type=float, default=12)

    measure = sub.add_parser("measure")
    measure.add_argument("--session", required=True)
    measure.add_argument("--baseline", required=True)
    measure.add_argument("--speed", type=float, required=True)
    measure.add_argument("--gap", type=float, required=True)
    measure.add_argument("--min-height", type=float, required=True)
    measure.add_argument("--out", required=True)

    args = parser.parse_args(argv)
    if args.command == "measure":
        _measure(args)
        return
    _import_lidar(args.ip_control)
    if args.command == "calibrate":
        _calibrate(args)
    else:
        _record(args)


def _import_lidar(root):
    if not os.path.isdir(root):
        raise SystemExit("找不到 IP_control：" + root)
    sys.path.insert(0, root)


def _calibrate(args):
    from sick_lidar import StreamReader, parse_scan
    from sick_lidar import ScanAverager

    reader = StreamReader(args.ip, args.port)
    reader.start()
    averager = ScanAverager()
    deadline = time.monotonic() + max(5.0, args.frames * 0.05 + 8.0)
    last_counter = None
    try:
        while averager.frames < args.frames and time.monotonic() < deadline:
            payload = reader.snapshot()["payload"]
            if payload is not None:
                scan = parse_scan(payload)
                if scan.counter != last_counter:
                    last_counter = scan.counter
                    averager.add(scan)
            time.sleep(0.002)
    finally:
        snapshot = reader.snapshot()
        reader.stop()
        reader.join(timeout=3)
    scan = averager.result()
    points = []
    if scan is not None:
        points = [xy for xy in scan.xy if xy is not None]
    blocked = calibration_link_error(snapshot["received"], len(points), snapshot["state"], args.ip, args.port)
    if blocked is not None:
        _write_calibration(args.out, False, blocked[0], blocked[1], 0.0, 0.0, 0.0, 0.0, 0.0)
        raise SystemExit(blocked[1])
    ok, a, b, c, distance, flatness, message = fit_roller_line(
        points, args.tolerance, args.range_deviation)
    code = "" if ok else "CAL_LINE_004"
    _write_calibration(args.out, ok, code, message, distance, flatness, a, b, c)
    if not ok:
        raise SystemExit(message or "滾輪校正失敗")


def _record(args):
    from sick_lidar import StreamReader, parse_scan

    os.makedirs(args.session, exist_ok=True)
    frames_path = os.path.join(args.session, "frames.jsonl")
    ready_path = os.path.join(args.session, "ready")
    failed_path = os.path.join(args.session, "failed")
    stop_path = os.path.join(args.session, "STOP")
    closed_path = os.path.join(args.session, "closed")
    reader = StreamReader(args.ip, args.port)
    writer = _FrameWriter(frames_path)
    reader.set_recorder(writer)
    reader.start()
    deadline = time.monotonic() + args.ready_timeout
    try:
        while time.monotonic() < deadline:
            if reader.snapshot()["received"] > 0:
                with open(ready_path, "w", encoding="utf-8") as handle:
                    handle.write("ok")
                break
            time.sleep(0.05)
        else:
            state = reader.snapshot()["state"]
            with open(failed_path, "w", encoding="utf-8") as handle:
                handle.write(state)
            raise SystemExit(state)
        while not os.path.exists(stop_path):
            time.sleep(0.05)
    finally:
        reader.stop()
        reader.join(timeout=3)
        writer.close()
        with open(closed_path, "w", encoding="utf-8") as handle:
            handle.write("ok")


def _measure(args):
    with open(args.baseline, encoding="utf-8") as handle:
        baseline = json.load(handle)
    parts = measure_recorded_frames(
        args.session,
        args.ip_control,
        baseline,
        args.speed,
        args.gap,
        args.min_height)
    _write_json(args.out, {"parts": parts})


def calibration_link_error(received, valid_points, state, ip, port):
    """沒收截面時不當成滾輪未清空。可擬合時回傳 None。"""
    if received <= 0:
        detail = (state or "").strip() or "沒有收到量測封包"
        return (
            "CAL_SYS_001",
            "線掃描器未連線或未開機（{}:{}）。{}".format(ip, port, detail),
        )
    if valid_points < 30:
        return (
            "CAL_SYS_001",
            "已連到線掃描器，但有效距離點只有 {} 點，無法校正。".format(valid_points),
        )
    return None


def _write_calibration(path, ok, code, message, distance, flatness, a, b, c):
    _write_json(path, {
        "ok": ok,
        "code": code,
        "message": message,
        "zDistance": distance,
        "flatnessMm": flatness,
        "lineA": a,
        "lineB": b,
        "lineC": c,
    })


def _write_json(path, payload):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False)


class _FrameWriter:
    """把每一圈寫成 jsonl。points 仍是有效點的 [X, Y]，給原本的 Python 尺寸路徑。rays 含每條射線編號，給 C#。counter、timestampUs 用來判斷平均區塊是否連續。由接收執行緒呼叫。"""

    def __init__(self, path):
        import threading
        self._lock = threading.Lock()
        self._file = open(path, "w", encoding="utf-8")
        self._parse = None

    def submit(self, payload, received_at):
        from sick_lidar import parse_scan
        scan = parse_scan(payload)
        points = []
        rays = []
        for index, xy in enumerate(scan.xy):
            if xy is None:
                rays.append([index, None, None])
            else:
                points.append([xy[0], xy[1]])
                rays.append([index, xy[0], xy[1]])
        row = {
            "t": received_at,
            "counter": scan.counter,
            "timestampUs": scan.timestamp_us,
            "points": points,
            "rays": rays,
        }
        text = json.dumps(row, ensure_ascii=False)
        with self._lock:
            self._file.write(text + "\n")
            self._file.flush()

    def close(self):
        with self._lock:
            self._file.close()


if __name__ == "__main__":
    main()
