"""Hank measure_parts：兩團沿 X 分開的點要分成兩件。"""
import json
import os
import tempfile
import unittest

from hank_line_measure import measure_recorded_frames

IP_CONTROL = r"D:\Project\CSBS\角鋼\Program\Hank\IP_control"


class HankMeasureTests(unittest.TestCase):
    def test_x_gap_splits_two_parts(self):
        baseline = {"lineA": 0.0, "lineB": -1.0, "lineC": 800.0}
        frames = [
            {"t": 0.0, "points": [[0, 790], [8, 790], [80, 790], [88, 790]]},
            {"t": 1.0, "points": [[0, 785], [8, 785], [80, 785], [88, 785]]},
        ]
        with tempfile.TemporaryDirectory() as session:
            with open(os.path.join(session, "frames.jsonl"), "w", encoding="utf-8") as handle:
                for frame in frames:
                    handle.write(json.dumps(frame) + "\n")
            parts = measure_recorded_frames(session, IP_CONTROL, baseline, 50, 10, 3)
        self.assertEqual([part["index"] for part in parts], [1, 2])
        self.assertAlmostEqual(parts[0]["width"], 8, delta=0.01)
        self.assertAlmostEqual(parts[0]["height"], 15, delta=0.01)
        self.assertAlmostEqual(parts[0]["leftLength"], 50, delta=0.01)
        self.assertAlmostEqual(parts[0]["rightLength"], 50, delta=0.01)
        self.assertAlmostEqual(parts[1]["width"], 8, delta=0.01)
        self.assertGreater(parts[1]["head"]["left"]["x"], parts[0]["head"]["right"]["x"])


if __name__ == "__main__":
    unittest.main()
