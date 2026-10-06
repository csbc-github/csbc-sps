"""合成截面：滾輪直線、並排、前後相接、頭尾平均。"""
import unittest

from line_scan_geometry import fit_roller_line, measure_frames
from line_scan_worker import calibration_link_error


def _roller_points():
    return [(x, 800.0) for x in range(-200, 201, 5)]


def _vee(center, height, half_width, toe_z):
    apex_z = 800.0 - height
    toe = 800.0 - toe_z
    points = []
    for step in range(8):
        x = center - half_width + step * (half_width / 8.0)
        z = toe + (apex_z - toe) * (step / 8.0)
        points.append((x, z))
    points.append((center, apex_z))
    for step in range(1, 9):
        x = center + step * (half_width / 8.0)
        z = apex_z + (toe - apex_z) * (step / 8.0)
        points.append((x, z))
    return points


class RollerLineTests(unittest.TestCase):
    def test_flat_roller_distance(self):
        ok, a, b, c, distance, flatness, _ = fit_roller_line(_roller_points(), 5, 15)
        self.assertTrue(ok)
        self.assertAlmostEqual(distance, 800, delta=0.1)
        self.assertLess(flatness, 1)
        self.assertAlmostEqual(c, 800, delta=0.1)
        self.assertAlmostEqual(a * 0 + b * 800 + c, 0, delta=0.1)

    def test_object_on_roller_is_not_clear(self):
        points = _roller_points() + _vee(0, 80, 40, 5)
        ok, *_rest = fit_roller_line(points, 5, 15)
        self.assertFalse(ok)

    def test_edge_foam_is_boundary_not_an_object(self):
        points = [(x, 800.0) for x in range(-200, 151, 5)]
        points += [(x, 780.0) for x in range(155, 201, 5)]
        ok, a, b, c, distance, flatness, message = fit_roller_line(points, 5, 15)
        self.assertTrue(ok, message)
        self.assertAlmostEqual(distance, 800, delta=0.5)
        self.assertLess(flatness, 1)

    def test_side_boards_or_floor_are_outside_roller_range(self):
        points = [(x, 700.0) for x in range(-200, -80, 5)]
        points += [(x, 800.0) for x in range(-80, 81, 5)]
        points += [(x, 860.0) for x in range(85, 201, 5)]
        ok, _a, _b, _c, distance, flatness, message = fit_roller_line(points, 5, 15)
        self.assertTrue(ok, message)
        self.assertAlmostEqual(distance, 800, delta=0.5)
        self.assertLess(flatness, 1)

    def test_range_deviation_decides_whether_nearby_floor_counts(self):
        points = [(x, 812.0) for x in range(-200, -120, 5)]
        points += [(x, 800.0) for x in range(-120, 121, 5)]
        points += [(x, 812.0) for x in range(125, 201, 5)]
        included, *_rest = fit_roller_line(points, 5, 15)
        self.assertFalse(included)
        excluded, _a, _b, _c, distance, _flatness, message = fit_roller_line(points, 5, 8)
        self.assertTrue(excluded, message)
        self.assertAlmostEqual(distance, 800, delta=0.5)


class MeasureTests(unittest.TestCase):
    def setUp(self):
        ok, self.a, self.b, self.c, *_rest = fit_roller_line(_roller_points(), 5, 15)
        self.assertTrue(ok)

    def test_one_bar_length_is_own_duration(self):
        frames = [
            {"t": 0.0, "points": _vee(0, 90, 40, 10)},
            {"t": 0.5, "points": _vee(0, 90, 40, 10)},
            {"t": 1.0, "points": _vee(0, 90, 40, 10)},
        ]
        parts = measure_frames(frames, (self.a, self.b, self.c), 100, 10, 3)
        self.assertEqual(len(parts), 1)
        part = parts[0]
        self.assertAlmostEqual(part["leftLength"], 100, delta=1)
        self.assertAlmostEqual(part["rightLength"], 100, delta=1)
        self.assertAlmostEqual(part["width"], 80, delta=2)
        self.assertAlmostEqual(part["height"], 80, delta=2)
        self.assertAlmostEqual(part["head"]["left"]["y"], 0, delta=0.1)
        self.assertAlmostEqual(part["tail"]["left"]["y"], 100, delta=1)

    def test_side_by_side_bars(self):
        frames = []
        for t in (0.0, 0.2):
            frames.append({"t": t, "points": _vee(-120, 90, 30, 8) + _vee(120, 70, 25, 6)})
        parts = measure_frames(frames, (self.a, self.b, self.c), 50, 10, 3)
        self.assertEqual(len(parts), 2)
        self.assertLess(parts[0]["head"]["apex"]["x"], parts[1]["head"]["apex"]["x"])

    def test_sequential_bars_split_on_travel_gap(self):
        frames = [
            {"t": 0.0, "points": _vee(0, 90, 40, 10)},
            {"t": 0.1, "points": _vee(0, 90, 40, 10)},
            {"t": 0.5, "points": []},
            {"t": 1.0, "points": []},
            {"t": 2.0, "points": _vee(0, 90, 40, 10)},
            {"t": 2.1, "points": _vee(0, 90, 40, 10)},
        ]
        parts = measure_frames(frames, (self.a, self.b, self.c), 100, 10, 3)
        self.assertEqual(len(parts), 2)

    def test_width_and_height_average_head_and_tail(self):
        frames = [
            {"t": 0.0, "points": _vee(0, 100, 40, 10)},
            {"t": 1.0, "points": _vee(0, 60, 20, 10)},
        ]
        parts = measure_frames(frames, (self.a, self.b, self.c), 10, 10, 3)
        self.assertEqual(len(parts), 1)
        head_h = parts[0]["head"]["height"]
        tail_h = parts[0]["tail"]["height"]
        self.assertAlmostEqual(parts[0]["height"], (head_h + tail_h) / 2.0, delta=0.01)
        head_w = parts[0]["head"]["width"]
        tail_w = parts[0]["tail"]["width"]
        self.assertAlmostEqual(parts[0]["width"], (head_w + tail_w) / 2.0, delta=0.01)
        self.assertGreater(head_w, tail_w)


class LinkErrorTests(unittest.TestCase):
    def test_scanner_off_is_not_roller_flatness(self):
        code, message = calibration_link_error(0, 0, "連線中斷", "192.168.0.1", 2111)
        self.assertEqual(code, "CAL_SYS_001")
        self.assertIn("未連線或未開機", message)
        self.assertIn("連線中斷", message)

    def test_enough_points_go_on_to_fit(self):
        self.assertIsNone(calibration_link_error(40, 80, "持續接收中", "192.168.0.1", 2111))


if __name__ == "__main__":
    unittest.main()
