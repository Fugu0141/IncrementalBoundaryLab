#!/usr/bin/env python3
"""Standalone tests for H4: run with `python research/h4_error_budget_selftest.py`."""
import unittest
from pathlib import Path
from h4_error_budget_experiment import (
    japanese_negative_streams, english_streams, spans, select_edges, metrics,
    construct_models, run,
)

class H4Tests(unittest.TestCase):
    def test_stream_gold_is_from_concatenation(self):
        xs=english_streams(['customer', 'monitor'])
        for raw,(start,end) in xs:
            self.assertEqual(raw[start:end], 'customer' if end-start==8 else 'monitor')

    def test_span_candidate_cut_rule(self):
        self.assertIn((6,14,'customer'),spans('kyouhacustomerno'))
        self.assertNotIn((6,14,'customer'),spans('kyouhacustomerxyz'))

    def test_cap_retains_highest_scoring_edges(self):
        e=[(0,9,'candidate',.1,False),(0,10,'candidatex',1.2,False),(0,11,'candidatexx',1.4,False)]
        self.assertEqual([r[2] for r in select_edges(e,threshold=.0,cap=2,mode='h4')], ['candidatexx','candidatex'])

    def test_calibration_metrics(self):
        p=[([(2,10,'customer',2.,False)],(2,10))]
        n=[[(2,10,'customer',-.5,False)]]
        a=metrics(p,n,threshold=0,cap=1,mode='h4')
        self.assertEqual(a['gold_recovered'],1)
        self.assertEqual(a['negatives_with_candidates'],0)

    def test_negative_streams_are_deterministic(self):
        v=['nihongo','miru','kaku']
        self.assertEqual(japanese_negative_streams(v,size=10),japanese_negative_streams(v,size=10))

    def test_dev_only_calibration_budget(self):
        # Full smoke test with real CMU and repository; negative development
        # budget has to be satisfied, but test budget is intentionally NOT asserted.
        root=Path(__file__).resolve().parent.parent
        r=run(root,20261010,0.05,180)
        self.assertLessEqual(r['dev_calibrated']['false_case_rate'],0.05)
        self.assertEqual(r['test_positive_streams'],54)
        self.assertEqual(r['test_negative_streams'],229)
        self.assertLessEqual(r['test']['h4_calibrated']['positive_spans'],54*4)
        ctx=construct_models(root,20261010)
        self.assertFalse(set(ctx['oov'])&set(ctx['edev']))

if __name__ == '__main__':
    unittest.main(verbosity=2)
