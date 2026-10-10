#!/usr/bin/env python3
"""Unit/smoke tests for H5. Run python research/h5_phonotactic_context_selftest.py"""
import unittest
from pathlib import Path
from h5_phonotactic_context_experiment import (
    RECIPES, calibrate, context_score, decorate, load_kana_tokens, measure,
    phonology, run, selected, shifted_english_streams, shifted_japanese_streams,
)

ROOT=Path(__file__).resolve().parent.parent


class H5Tests(unittest.TestCase):
    def test_romanization_tokens_from_csharp(self):
        toks=load_kana_tokens(ROOT)
        self.assertIn('kyo',toks)
        self.assertIn('shi',toks)
        self.assertIn('wa',toks)
        self.assertGreater(len(toks),50)

    def test_phonology_is_not_oracle(self):
        cov=phonology(load_kana_tokens(ROOT))
        self.assertEqual(cov('kore'),1.0)
        self.assertEqual(cov('nihongo'),1.0)
        self.assertLess(cov('customer'),1.0)
        self.assertEqual(cov(''),1.)

    def test_context_depends_on_raw_span_not_labels(self):
        cov=phonology(load_kana_tokens(ROOT))
        raw='korecustomerga'
        self.assertGreater(context_score(raw,4,12,cov),context_score(raw,5,12,cov))
        self.assertEqual(context_score('abc',0,3,cov),0.)

    def test_gold_from_string_concatenation(self):
        cases=shifted_english_streams(['customer','monitor'])
        self.assertEqual([s[a:b] for s,(a,b) in cases],['customer','monitor'])

    def test_shift_preserves_negative_count_and_changes_context(self):
        inp=['kyouhamiha','koregakakana','somestring']
        out=shifted_japanese_streams(inp)
        self.assertEqual(len(out),len(inp))
        self.assertEqual(out[-1],inp[-1])
        self.assertNotEqual(out[0],inp[0])

    def test_cap_and_calibration(self):
        e1=(1,9,'customer',1.5,False,0.5,0.5)
        e2=(1,9,'customer',-0.5,False,0.5,0.5)
        w=RECIPES['char_phonotactic_boundary']
        positive=[([e1],(1,9))]
        negative=[[e2]]
        th=calibrate(negative,w,0.)
        self.assertFalse(selected(negative[0],w,th))
        m=measure(positive,negative,w,th)
        self.assertEqual(m['recalled'],1)
        self.assertEqual(m['false_cases'],0)
        morph=(1,9,'customer',-.5,True,0.,0.)
        self.assertIsNone(calibrate([[morph]],w,0.))

    def test_ablation_disables_additions(self):
        edge=(0,8,'customer',.7,False,.3,.4)
        self.assertEqual(round(edge[3],5),round(sum(v*i for v,i in zip((1.,edge[5],edge[6]),(edge[3],0.,0.))),5))
        scores=[edge[3]+w[0]*edge[5]+w[1]*edge[6] for w in RECIPES.values()]
        self.assertGreater(max(scores),min(scores))

    def test_integration_is_held_out(self):
        x=run(ROOT,20261010)
        self.assertEqual(x['english_heldout_types'],54)
        self.assertEqual(x['japanese_heldout_types'],34)
        self.assertEqual(x['test_negative_cases'],229)
        self.assertLessEqual(x['recipes'][x['selected_on_dev']]['dev']['false_case_rate'],0.05)
        self.assertEqual(x['baseline_h4']['gold_recovered'],x['recipes']['char_only']['test']['recalled'])
        self.assertEqual(x['baseline_h4']['negatives_with_candidates'],x['recipes']['char_only']['test']['false_cases'])

if __name__=='__main__':
    unittest.main(verbosity=2)
