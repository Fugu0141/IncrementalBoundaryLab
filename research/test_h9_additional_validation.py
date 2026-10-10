"""H9 follow-up test harness: properties of new corpus and stage diagnostics."""
import functools
import unittest
from pathlib import Path
from h9_additional_validation import cases_for_seed, stage, run_seed
from h5_phonotactic_context_experiment import construct_models, phonology, load_kana_tokens, decorate, score_edges
from h6_boundary_alias_experiment import romaji_table, as_kana, ALIASES
from h9_joint_lattice_experiment import joint_path

ROOT=Path(__file__).resolve().parent.parent

class H9ExtraTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        ctx=construct_models(ROOT,20261010)
        cls.cases=cases_for_seed(ctx['oov'],ctx['jtest']+ctx['phon'])
        cls.table=romaji_table(ROOT)
        cls.cov=staticmethod(phonology(load_kana_tokens(ROOT)))

    def test_case_count(self):
        self.assertEqual(len(self.cases),1094)

    def test_unique_raw(self):
        self.assertEqual(len(set(c['raw'] for c in self.cases)),1094)

    def test_new_strata(self):
        self.assertEqual(len(set(c['group'] for c in self.cases)),12)

    def test_offsets(self):
        for c in self.cases:
            for start,end in c['gold']:
                self.assertTrue(0<=start<end<=len(c['raw']))
                self.assertTrue(c['raw'][start:end])

    def test_negative_cases(self):
        self.assertTrue(all(not c['gold'] for c in self.cases if c['group']=='japanese_only_shift'))

    def test_four_island_cap_is_structural(self):
        pos=[c for c in self.cases if c['group']=='four_islands_cap']
        self.assertEqual(len(pos),54)
        self.assertTrue(all(len(c['gold'])==4 for c in pos))
        self.assertEqual(joint_path('customerga'*4, [], self.cov,self.table,'phonetic_only',0.,0.),[])

    def test_romanization_pair_offsets(self):
        pairs={}
        for c in self.cases:
            if c['pair_id']:pairs.setdefault(c['pair_id'],[]).append(c)
        self.assertEqual(len(pairs),54)
        for p in pairs.values():
            self.assertEqual(len(p),2)
            a,b=p
            self.assertEqual(a['raw'][slice(*a['gold'][0])],b['raw'][slice(*b['gold'][0])])

    def test_romanization_readings(self):
        for a,b,c,d in ALIASES:
            self.assertEqual(as_kana(a,self.table),as_kana(b,self.table))
            self.assertEqual(as_kana(c,self.table),as_kana(d,self.table))

    def test_symbol_cases_not_misrepresented_as_improved(self):
        self.assertEqual(sum(c['group']=='symbol_new_context' for c in self.cases),54)

    def test_case_intent_is_not_guessed(self):
        self.assertEqual(sum(c['group']=='short_intent_english' for c in self.cases),32)

    def test_repeatable_decoding_small_slice(self):
        c=[x for x in self.cases if x['group']=='two_islands_new'][0]
        ctx=construct_models(ROOT,20261010)
        es=decorate(c['raw'],score_edges(c['raw'],ctx['diff']),self.cov)
        out=[]
        for _ in range(2):out.append(joint_path(c['raw'],es,self.cov,self.table,'phonetic_only',1.185707,.04,.02,.5,0.))
        self.assertEqual(out[0],out[1])

    def test_long_input_probe_known_limit(self):
        import sys
        raw='ka'*600
        with self.assertRaises(RecursionError):
            joint_path(raw,[],self.cov,self.table,'phonetic_only',1.,0.,.02,.5,0.)

if __name__=='__main__':unittest.main(verbosity=2)
