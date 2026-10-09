import unittest

from evaluate import aggregate, boundary_counts, edit_distance, evaluate, paired_delta


class EvaluationTests(unittest.TestCase):
    def test_edit_distance(self):
        self.assertEqual(edit_distance("今日は", "今日は"), 0)
        self.assertEqual(edit_distance("今日は", "今日"), 1)
        self.assertEqual(edit_distance("abc", "axc"), 1)
        self.assertEqual(edit_distance("", "日"), 1)

    def test_micro_boundary_counts(self):
        self.assertEqual(boundary_counts([2, 5], [2, 6]), (1, 1, 1))

    def test_rejects_unverified_gold(self):
        corpus = [{"id": "x", "raw": "abc",
                   "gold_status": "unconfirmed",
                   "gold_output": None}]
        observations = [{"case_id": "x", "condition": "a",
                         "observed_output": "abc"}]
        rows, ignored = evaluate(corpus, observations)
        self.assertEqual(rows, {})
        self.assertEqual(ignored, 1)

    def test_detects_duplicates(self):
        corpus = [{"id": "x", "raw": "abc",
                   "gold_status": "verified", "gold_output": "abc"}]
        obs = [{"case_id": "x", "condition": "a",
                "observed_output": "abc"}] * 2
        with self.assertRaisesRegex(ValueError, "duplicate"):
            evaluate(corpus, obs)

    def test_paired_comparison_reports_improvement(self):
        corpus = [
            {"id": "x", "raw": "abc",
             "gold_status": "verified", "gold_output": "abc"},
            {"id": "y", "raw": "def",
             "gold_status": "verified", "gold_output": "def"},
        ]
        obs = [
            {"case_id": "x", "condition": "old", "observed_output": "axc"},
            {"case_id": "x", "condition": "new", "observed_output": "abc"},
            {"case_id": "y", "condition": "old", "observed_output": "dyf"},
            {"case_id": "y", "condition": "new", "observed_output": "def"},
        ]
        rows, ignored = evaluate(corpus, obs)
        self.assertEqual(ignored, 0)
        self.assertAlmostEqual(aggregate([v for (c, _), v in rows.items()
                                          if c == "old"])["cer"], 1 / 3)
        result = paired_delta(rows, "old", "new", 100, 12)
        self.assertEqual(result["matched_n"], 2)
        self.assertAlmostEqual(result["delta_cer"], 1 / 3)
        self.assertEqual(result["bootstrap_95ci"], [1 / 3, 1 / 3])


if __name__ == "__main__":
    unittest.main()
