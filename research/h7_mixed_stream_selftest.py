#!/usr/bin/env python3
"""Self-tests for H7 systematic generator and boundary audit; no Windows IME involved."""
import unittest
from pathlib import Path
from h7_mixed_stream_stress import generate_cases,structural_fuzz,metric,h7_bonus,h7_select,h7_calibrate
from h6_boundary_alias_experiment import romaji_table,ALIASES
from h5_phonotactic_context_experiment import construct_models,phonology,load_kana_tokens
ROOT=Path(__file__).resolve().parent.parent

class MixedStressTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ctx=construct_models(ROOT,20261010)
        cls.mapping=romaji_table(ROOT)
        cls.cases=generate_cases(cls.ctx['oov'],cls.ctx['jtest']+cls.ctx['phon'],cls.mapping,ALIASES)

    def test_categories(self):
        self.assertEqual(len(set(c['group'] for c in self.cases)),13)

    def test_case_count(self):
        self.assertEqual(len(self.cases),5686)

    def test_offsets(self):
        for c in self.cases:
            for st,en in c['gold']:
                self.assertTrue(0<=st<en<=len(c['raw']))

    def test_adjacent_english_no_fake_transition(self):
        for c in self.cases:
            if c['group']=='adjacent_english':
                self.assertEqual(len(c['switches']),2)

    def test_english_only_has_no_switch(self):
        for c in self.cases:
            if c['group']=='english_only':
                self.assertEqual(c['switches'],[])

    def test_no_english_has_no_gold_spans(self):
        for c in self.cases:
            if c['group']=='no_english':
                self.assertFalse(c['gold'])

    def test_alias_pair_gold_offsets(self):
        pairs={}
        for c in self.cases:
            if c['pair']:
                pairs.setdefault(c['pair'],[]).append(c)
        self.assertEqual(len(pairs),324)
        for p in pairs.values():
            self.assertEqual(len(p),2)
            a,b=p
            self.assertEqual(a['raw'][slice(*a['gold'][0])],b['raw'][slice(*b['gold'][0])])

    def test_metric_exact_switches(self):
        c={'raw':'orehastuffga','gold':[[5,10]],'switches':[5,10]}
        d=metric(c,[(5,10)])
        self.assertEqual((d['tp'],d['fp'],d['fn']),(2,0,0))

    def test_empty_context_not_penalized(self):
        cov=phonology(load_kana_tokens(ROOT))
        self.assertEqual(h7_bonus((0,8),'customer',cov),0.)

    def test_budget_calibration(self):
        cov=phonology(load_kana_tokens(ROOT))
        self.assertEqual(h7_calibrate([('oreha',[])],cov,0.),-1e6)

    def test_randomized_generator_gold(self):
        x=structural_fuzz(self.mapping,20261010,3000)
        self.assertEqual(x['cases'],3000)
        self.assertEqual(x['invariant_failures'],0)

if __name__=='__main__':
    unittest.main(verbosity=2)
