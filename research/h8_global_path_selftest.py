#!/usr/bin/env python3
"""Unit and regression tests for the H8 multi-span offline research experiment."""
import unittest
from pathlib import Path
from h8_global_path_experiment import (
    build_development_cases, calibrate_global, edge_value, global_path,
    score_case, score_development, result_for_seed, single_h7)
from h5_phonotactic_context_experiment import phonology,load_kana_tokens
ROOT=Path(__file__).resolve().parent.parent


def edge(st,en,score,shape=False):
    # H5 decorated edge: start,end,word,char_score,morph,unreadable,context
    return (st,en,'x'*(en-st),score,shape,0.,0.)

class H8PathTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.cov=phonology(load_kana_tokens(ROOT))

    def test_nonoverlapping_multi_span(self):
        raw='a'*25
        edges=[edge(2,9,3),edge(12,20,3)]
        self.assertEqual(global_path(raw,edges,self.cov,'phonetic_only',1.0),[(2,9),(12,20)])

    def test_disallow_overlapping_spans(self):
        raw='a'*24
        es=[edge(2,14,3.),edge(8,20,3.)]
        self.assertEqual(len(global_path(raw,es,self.cov,'phonetic_only',1.0)),1)

    def test_tied_path_is_deterministic(self):
        raw='a'*25;es=[edge(1,11,2.),edge(12,20,2.)]
        first=global_path(raw,es,self.cov,'phonetic_only',1.0)
        self.assertEqual(first,global_path(raw,list(reversed(es)),self.cov,'phonetic_only',1.0))

    def test_min_score_absent_gives_japanese_only(self):
        self.assertEqual(global_path('a'*25,[edge(4,12,.2)],self.cov,'phonetic_only',1.),[])

    def test_dev_budget_calibrates_without_gold(self):
        dev=[('a'*15,[edge(2,10,.2)]),('b'*15,[edge(2,10,.8)]),('c'*15,[edge(2,10,1.2)])]
        threshold=calibrate_global(dev,self.cov,'phonetic_only',0.,0.0)
        self.assertEqual(sum(bool(global_path(raw,es,self.cov,'phonetic_only',threshold)) for raw,es in dev),0)

    def test_morphology_override_can_fail_strict_budget(self):
        self.assertIsNone(calibrate_global([('abcdefghij',[edge(1,9,0.,True)])],self.cov,'phonetic_only',0.,0.))

    def test_gold_span_metric_with_two_english_islands(self):
        c={'raw':'nihonabcdefghganetworkni','gold':[[5,13],[15,22]],'switches':[5,13,15,22]}
        m=score_case(c,[(5,13),(15,22)])
        self.assertTrue(m['exact'])
        self.assertEqual(m['hit_spans'],2)

    def test_english_only_zero_switch_but_exact(self):
        c={'raw':'customer','gold':[[0,8]],'switches':[]}
        m=score_case(c,[(0,8)])
        self.assertTrue(m['exact']);self.assertEqual(m['tp'],0)

    def test_false_japanese_case(self):
        c={'raw':'kyouha','gold':[],'switches':[]}
        self.assertTrue(score_case(c,[(0,6)])['false_case'])
        self.assertTrue(score_case(c,[])['exact'])

    def test_development_spans_raw_offsets(self):
        cases=build_development_cases(['customer','monitor','strategy'])
        self.assertEqual(len(cases),13)
        for c in cases:
            for a,b in c['gold']:self.assertTrue(0<=a<b<=len(c['raw']))

    def test_dev_macro_equal_group_weight(self):
        cases=build_development_cases(['customer','monitor','strategy'])
        value,groups=score_development(cases,lambda raw: [])
        self.assertEqual(value,0)
        self.assertEqual(set(groups),{'start','middle','end','english_only','two_islands'})

    def test_empty_input_and_max_islands(self):
        self.assertEqual(global_path('',[],self.cov,'phonetic_only',0),[])
        raw='x'*45
        es=[edge(0,8,2),edge(10,18,2),edge(20,28,2),edge(30,38,2)]
        result=global_path(raw,es,self.cov,'phonetic_only',1.,max_islands=3)
        self.assertEqual(len(result),3)

    def test_smoke_reproducible_real_model(self):
        from h8_global_path_experiment import build_development_cases
        from h5_phonotactic_context_experiment import construct_models
        ctx=construct_models(ROOT,20261010)
        cases=[{'id':0,'raw':'customerga','gold':[[0,8]],'switches':[8],'group':'start','pair':None}]
        result=result_for_seed(ROOT,cases,20261010)
        self.assertIn('H8_global',result['results'])
        self.assertEqual(result['model_development_english_types'],180)

if __name__=='__main__':unittest.main(verbosity=2)
