"""把 SICK 截面收成規格 v1.1 的頭尾角點。只依賴標準庫。"""
import math


def fit_roller_line(points, tolerance_mm, range_deviation_mm):
    """擬合空滾輪直線。原點到直線的距離是掃描器到滾輪的 Z。

    離開平面超過 range_deviation_mm 的點視為滾輪外（兩側高板或外側地板），
    不參與平整判定。滾輪範圍內仍用 tolerance_mm 判斷是否清空。
    回傳 (ok, a, b, c, z_distance, flatness_mm, message)。
    直線 ax+by+c=0 且 a^2+b^2=1、c>0，點的高度是 ax+by+c。
    """
    if tolerance_mm <= 0 or not math.isfinite(tolerance_mm):
        raise ValueError("平整容許值必須大於 0")
    if range_deviation_mm <= 0 or not math.isfinite(range_deviation_mm):
        raise ValueError("滾輪範圍誤差必須大於 0")
    usable = [(float(x), float(y)) for x, y in points if _finite2(x, y)]
    if len(usable) < 30:
        return False, 0.0, 0.0, 0.0, 0.0, 0.0, "有效點太少，無法判斷滾輪是否清空"
    rough_a, rough_b, _ = _least_squares_line(usable)
    ordered = sorted(usable, key=lambda point: (-rough_b) * point[0] + rough_a * point[1])
    seed_count = min(len(ordered), max(10, len(ordered) // 5))
    seed_start = (len(ordered) - seed_count) // 2
    a, b, c = _least_squares_line(ordered[seed_start:seed_start + seed_count])
    left = seed_start
    right = seed_start + seed_count - 1
    while left > 0 and abs(a * ordered[left - 1][0] + b * ordered[left - 1][1] + c) <= range_deviation_mm:
        left -= 1
    while right + 1 < len(ordered) and abs(
            a * ordered[right + 1][0] + b * ordered[right + 1][1] + c) <= range_deviation_mm:
        right += 1
    if right - left + 1 < 30:
        return _roller_reject(a, b, c, ordered)
    interior = ordered[left:right + 1]
    a, b, c = _least_squares_line(interior)
    residuals = sorted(abs(a * x + b * y + c) for x, y in interior)
    limit = max(1, int(len(residuals) * 0.95) - 1)
    flatness = residuals[limit]
    within = sum(1 for value in residuals if value <= tolerance_mm)
    if flatness > tolerance_mm or within / len(residuals) < 0.85:
        return False, a, b, c, c, flatness, "輪廓變異超過容許值，判定滾輪尚未清空"
    return True, a, b, c, c, flatness, ""


def measure_frames(frames, line, speed_mm_s, gap_mm, min_height_mm):
    """依橫向空隙與沿滾輪空檔分件，頭是第一個截面、尾是最後一個。

    frames 為 [{"t": 秒, "points": [[感測器x, 感測器y], ...]}]。
    Y 為 (t - 第一幀) * speed。width、height 取頭尾平均。
    """
    if speed_mm_s <= 0 or not math.isfinite(speed_mm_s):
        raise ValueError("滾輪速度必須大於 0")
    if gap_mm <= 0 or not math.isfinite(gap_mm):
        raise ValueError("分件空隙必須大於 0")
    a, b, c = line
    if not frames:
        return []
    t0 = float(frames[0]["t"])
    tracks = []
    finished = []
    for frame in frames:
        t = float(frame["t"])
        segments = _segments(frame.get("points") or [], a, b, c, t, t0, speed_mm_s, gap_mm, min_height_mm)
        _assign(tracks, finished, segments, t, speed_mm_s, gap_mm)
    for track in tracks:
        _close(track, finished)
    finished.sort(key=lambda part: (part["head"]["apex"]["x"], part["head"]["left"]["x"]))
    result = []
    for index, part in enumerate(finished, 1):
        head = part["head"]
        tail = part["tail"]
        result.append({
            "index": index,
            "head": head,
            "tail": tail,
            "width": (head["width"] + tail["width"]) / 2.0,
            "height": (head["height"] + tail["height"]) / 2.0,
            "leftLength": _distance(head["left"], tail["left"]),
            "rightLength": _distance(head["right"], tail["right"]),
        })
    return result


def _assign(tracks, finished, segments, t, speed, gap_mm):
    pairs = []
    for ti, track in enumerate(tracks):
        for si, segment in enumerate(segments):
            delta = abs(segment["apex"]["x"] - track["x"])
            if delta <= gap_mm:
                pairs.append((delta, ti, si))
    pairs.sort()
    used_tracks = set()
    used_segments = set()
    for _, ti, si in pairs:
        if ti in used_tracks or si in used_segments:
            continue
        used_tracks.add(ti)
        used_segments.add(si)
        _append_sample(tracks[ti], segments[si], t)
    for ti, track in enumerate(list(tracks)):
        if ti not in used_tracks and (t - track["last_t"]) * speed > gap_mm:
            _close(track, finished)
            tracks.remove(track)
    for si, segment in enumerate(segments):
        if si not in used_segments:
            tracks.append({
                "x": segment["apex"]["x"],
                "last_t": t,
                "samples": [segment],
            })


def _append_sample(track, segment, t):
    track["samples"].append(segment)
    track["last_t"] = t
    track["x"] = segment["apex"]["x"]


def _close(track, finished):
    samples = track["samples"]
    if len(samples) < 2:
        return
    finished.append({"head": samples[0], "tail": samples[-1]})


def _segments(points, a, b, c, t, t0, speed, gap_mm, min_height_mm):
    y = (t - t0) * speed
    projected = []
    for x, z in points:
        if not _finite2(x, z):
            continue
        along, normal = _project(x, z, a, b, c)
        height = a * x + b * z + c
        if height >= min_height_mm:
            projected.append((along, y, height, normal))
    projected.sort(key=lambda row: row[0])
    runs = []
    current = []
    for row in projected:
        if current and row[0] - current[-1][0] > gap_mm:
            runs.append(current)
            current = []
        current.append(row)
    if current:
        runs.append(current)
    segments = []
    for run in runs:
        segment = _corner_segment(run)
        if segment is not None:
            segments.append(segment)
    return segments


def _corner_segment(run):
    if len(run) < 6:
        return None
    apex = max(run, key=lambda row: row[2])
    left = [row for row in run if row[0] < apex[0] - 0.5]
    right = [row for row in run if row[0] > apex[0] + 0.5]
    if len(left) < 3 or len(right) < 3:
        return None
    left_corner = _toe(left, min)
    right_corner = _toe(right, max)
    if left_corner[0] >= right_corner[0]:
        return None
    left_point = _point(left_corner)
    right_point = _point(right_corner)
    apex_point = _point(apex)
    return {
        "left": left_point,
        "right": right_point,
        "apex": apex_point,
        "width": _distance(left_point, right_point),
        "height": apex_point["z"] - (left_point["z"] + right_point["z"]) / 2.0,
    }


def _toe(points, outer):
    floor = min(row[2] for row in points)
    near = [row for row in points if row[2] <= floor + 2.0]
    return outer(near, key=lambda row: row[0])


def _project(x, z, a, b, c):
    foot_x = -c * a
    foot_z = -c * b
    along = (x - foot_x) * (-b) + (z - foot_z) * a
    return along, a * x + b * z + c


def _roller_reject(a, b, c, points):
    residuals = sorted(abs(a * x + b * y + c) for x, y in points)
    limit = max(1, int(len(residuals) * 0.95) - 1)
    return False, a, b, c, c, residuals[limit], "輪廓變異超過容許值，判定滾輪尚未清空"


def _least_squares_line(points):
    count = float(len(points))
    mean_x = sum(x for x, _ in points) / count
    mean_y = sum(y for _, y in points) / count
    sxx = syy = sxy = 0.0
    for x, y in points:
        dx = x - mean_x
        dy = y - mean_y
        sxx += dx * dx
        syy += dy * dy
        sxy += dx * dy
    angle = 0.5 * math.atan2(2.0 * sxy, sxx - syy)
    tangent_x = math.cos(angle)
    tangent_y = math.sin(angle)
    a = -tangent_y
    b = tangent_x
    norm = math.hypot(a, b)
    a /= norm
    b /= norm
    c = -(a * mean_x + b * mean_y)
    if c < 0:
        a, b, c = -a, -b, -c
    return a, b, c


def _point(row):
    return {"x": row[0], "y": row[1], "z": row[2]}


def _distance(first, second):
    return math.dist(
        (first["x"], first["y"], first["z"]),
        (second["x"], second["y"], second["z"]))


def _finite2(x, y):
    return math.isfinite(x) and math.isfinite(y)
