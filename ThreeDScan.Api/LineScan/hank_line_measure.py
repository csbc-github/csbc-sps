"""把錄製截面編成 Hank measure_parts 要的 3D 點，再呼叫他的分件。"""
import json
import os


def measure_recorded_frames(session, ip_control, baseline, speed, gap_mm, min_height_mm):
    """回傳與 C# 改寫相同形狀的 parts。尺寸定義來自 sick_dimensions.measure_parts。"""
    import sys
    sys.path.insert(0, ip_control)
    from sick_dimensions import measure_parts

    ply_path = os.path.join(session, "hank_cloud.ply")
    count = _write_ply(os.path.join(session, "frames.jsonl"), baseline, speed, min_height_mm, ply_path)
    if count == 0:
        return []
    report = measure_parts(ply_path, gap_mm=gap_mm)
    return [_to_api(part) for part in report["parts"]]


def _write_ply(frames_path, baseline, speed, min_height_mm, ply_path):
    count = 0
    for _point in _projected(frames_path, baseline, speed, min_height_mm):
        count += 1
    header = (
        "ply\n"
        "format ascii 1.0\n"
        "comment geometry 3D_manual_translation_estimated\n"
        "comment recording_complete 1\n"
        "element vertex {}\n"
        "property float x\n"
        "property float y\n"
        "property float z\n"
        "property uint frame_id\n"
        "end_header\n"
    ).format(count)
    written = 0
    with open(ply_path, "w", encoding="ascii", newline="\n") as handle:
        handle.write(header)
        for along, travel, height, frame_id in _projected(frames_path, baseline, speed, min_height_mm):
            handle.write("{} {} {} {}\n".format(along, travel, height, frame_id))
            written += 1
    if written != count:
        raise RuntimeError("寫入 PLY 的點數與計數不一致")
    return count


def _projected(frames_path, baseline, speed, min_height_mm):
    line = (float(baseline["lineA"]), float(baseline["lineB"]), float(baseline["lineC"]))
    first = None
    frame_id = 0
    with open(frames_path, encoding="utf-8") as handle:
        for raw in handle:
            text = raw.strip()
            if not text:
                continue
            frame = json.loads(text)
            time = float(frame["t"])
            if first is None:
                first = time
            travel = (time - first) * speed
            for point in frame.get("points") or []:
                sensor_x = float(point[0])
                sensor_y = float(point[1])
                along, height = _project(sensor_x, sensor_y, line)
                if height >= min_height_mm:
                    yield along, travel, height, frame_id
            frame_id += 1


def _project(sensor_x, sensor_y, line):
    line_a, line_b, line_c = line
    foot_x = -line_c * line_a
    foot_y = -line_c * line_b
    along = (sensor_x - foot_x) * (-line_b) + (sensor_y - foot_y) * line_a
    height = line_a * sensor_x + line_b * sensor_y + line_c
    return along, height


def _to_api(part):
    """把包絡編成規格角點。高是最高 Z，左右長都是 Y 跨度。"""
    min_x, min_y, _min_z = part["min_xyz_mm"]
    max_x, max_y, _max_z = part["max_xyz_mm"]
    height = part["height_mm"]
    mid_x = (min_x + max_x) / 2.0

    def point(x, y, z):
        return {"x": x, "y": y, "z": z}

    return {
        "index": part["part_id"],
        "head": {
            "left": point(min_x, min_y, 0.0),
            "right": point(max_x, min_y, 0.0),
            "apex": point(mid_x, min_y, height),
        },
        "tail": {
            "left": point(min_x, max_y, 0.0),
            "right": point(max_x, max_y, 0.0),
            "apex": point(mid_x, max_y, height),
        },
        "width": part["width_mm"],
        "height": height,
        "leftLength": part["length_mm"],
        "rightLength": part["length_mm"],
    }
