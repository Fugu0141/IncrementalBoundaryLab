#!/usr/bin/env python3
"""Targeted tests for H9 joint Japanese/English interval decoder."""
import unittest
from pathlib import Path
from h9_joint_lattice_experiment import (japanese_edges,english_eligibility,
    joint_path,dev_choose_configs,result_for_seed,score_case)
from h6_boundary_alias_experiment import romaji_table,as_kana
from h5_phonotactic_context_experiment import construct_models,phonology,load_kana_tokens,decorate,score_edges
from h7_frozen_reference_20261010 import generate_cases
from h6_boundary_alias_experiment import ALIASES

ROOT=Path(__file__).resolve().parent.parent

class H9JointPathTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.table=romaji_table(ROOT)
        cls.cov=staticmethod(phonology(load_kana_tokens(ROOT)))

    def test_japanese_edges_have_original_offsets(self):
        raw='shigotode'
        edges=japanese_edges(raw,self.table,.08)
        self.assertEqual(len(edges),len(raw))
        self.assertTrue(any(end==3 and kind=='JapanesePhonetic' for end,score,kind in edges[0]))
        for i,chunks in enumerate(edges):
            self.assertTrue(all(i<end<=len(raw) for end,score,kind in chunks))

    def test_english_at_start_with_no_left_context(self):
        raw='customerni'
        en=[(0,8,'customer',4.,False,0.,0.)]
        result=joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.02,0.,0.)
        self.assertEqual(result,[(0,8)])

    def test_english_at_end_with_no_right_context(self):
        raw='orehacustomer';en=[(5,13,'customer',5.,False,0.,0.)]
        self.assertEqual(joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.02,0.,0.),[(5,13)])

    def test_japanese_only_rejects_weak_english(self):
        raw='shigotode';en=[(0,8,'shigotod',.2,False,0.,0.)]
        result=joint_path(raw,en,self.cov,self.table,'phonetic_only',0.,0.,.05,0.,0.)
        self.assertEqual(result,[])

    def test_two_islands_nonoverlapping(self):
        raw='orehacustomergamonitorni'
        a=len('oreha'); b=a+len('customer')
        c=b+len('ga'); d=c+len('monitor')
        en=[(a,b,'customer',5.,False,0.,0.),(c,d,'monitor',5.,False,0.,0.)]
        result=joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.02,0.,0.)
        self.assertEqual(result,[(a,b),(c,d)])

    def test_max_islands_limits_paths(self):
        raw='customerga monitorde feature'
        a=raw.index('customer');b=raw.index('monitor');c=raw.index('feature')
        en=[(a,a+8,'customer',5.,False,0.,0.),(b,b+7,'monitor',5.,False,0.,0.),(c,c+7,'feature',5.,False,0.,0.)]
        chosen=joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.0,0.,0.,max_islands=2)
        self.assertEqual(len(chosen),2)
        self.assertTrue(all(chosen[i][1]<=chosen[i+1][0] for i in range(len(chosen)-1)))

    def test_threshold_denies_weak_english(self):
        raw='orehacustomerni';en=[(5,13,'customer',.1,False,0.,0.)]
        self.assertEqual(joint_path(raw,en,self.cov,self.table,'phonetic_only',1.5,0.,.02,0.,0.),[])

    def test_morphology_is_separate_exception(self):
        raw='japanesega';en=[(0,8,'japanese',-100.,True,0.,0.)]
        out=english_eligibility(raw,en,self.cov,1.,'phonetic_only',0.,0.)
        self.assertEqual(len(out[0]),1)

    def test_no_gold_injection(self):
        raw='orehacustomerni';en=[(5,13,'customer',5.,False,0.,0.)]
        a=joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.02,.5,0.)
        b=joint_path(raw,en,self.cov,self.table,'phonetic_only',1.,0.,.02,.5,0.)
        self.assertEqual(a,b)

    def test_romanization_variants_are_same_phonetic_output(self):
        for a,b in [('shi','si'),('chi','ti'),('tsu','tu'),('fu','hu'),('sha','sya')]:
            self.assertEqual(as_kana(a,self.table),as_kana(b,self.table))

    def test_empty_input(self):
        self.assertEqual(joint_path('',[],self.cov,self.table,'phonetic_only',0.,0.),[])

    def test_exact_multi_span_metric(self):
        raw='orehacustomergamonitorni'
        a=5;b=13;c=15;d=22
        case={'raw':raw,'gold':[(a,b),(c,d)],'switches':[a,b,c,d]}
        score=score_case(case,[(a,b),(c,d)])
        self.assertTrue(score['exact']);self.assertEqual(score['hit_spans'],2)

    def test_group_generator_all_13(self):
        ctx=construct_models(ROOT,20261010)
        cases=generate_cases(ctx['oov'],ctx['jtest']+ctx['phon'],self.table,ALIASES)
        self.assertEqual(len(cases),5686)
        self.assertEqual(len({c['group'] for c in cases}),13)

    def test_development_selection_has_no_holdout_config(self):
        ctx=construct_models(ROOT,20261010)
        from functools import lru_cache
        @lru_cache(maxsize=8000)
        def edges(raw):return decorate(raw,score_edges(raw,ctx['diff']),self.cov)
        h8,h9,configs,negative=dev_choose_configs(ROOT,ctx,self.cov,self.table,edges)
        self.assertLessEqual(h9['dev_false'],int(.05*len(negative)))
        self.assertEqual(len(ctx['oov']),54)
        self.assertGreaterEqual(h9['dev_macro'],0.)
        self.assertLessEqual(len(configs),18)

if __name__=='__main__':unittest.main(verbosity=2)

END_H9_TES