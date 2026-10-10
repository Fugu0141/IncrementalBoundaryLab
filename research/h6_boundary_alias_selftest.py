#!/usr/bin/env python3
"""Unit/smoke checks for H6 raw boundary coordinates and romaji aliases."""
import unittest
from pathlib import Path
from h6_boundary_alias_experiment import (
 ALIASES, as_kana, romaji_table, prepare_alias_corpus, score_boundary,
 h6_select, h6_calibrate, metrics, run)
ROOT=Path(__file__).resolve().parent.parent

class H6Tests(unittest.TestCase):
 def test_csharp_table_contains_both_romanization_styles(self):
  t=romaji_table(ROOT)
  for x,y in [('shi','si'),('chi','ti'),('tsu','tu'),('fu','hu'),('sha','sya'),('sho','syo'),('cha','tya')]:
   self.assertEqual(t[x],t[y],(x,y))

 def test_equivalence_of_actual_japanese_contexts(self):
  t=romaji_table(ROOT)
  for p,q,s,r in ALIASES:
   self.assertEqual(as_kana(p,t),as_kana(q,t),(p,q))
   self.assertEqual(as_kana(s,t),as_kana(r,t),(s,r))
   self.assertIsNotNone(as_kana(p,t))

 def test_multi_pattern_examples(self):
  t=romaji_table(ROOT)
  self.assertEqual(as_kana('shigoto',t),as_kana('sigoto',t))
  self.assertEqual(as_kana('chikai',t),as_kana('tikai',t))
  self.assertEqual(as_kana('tsukau',t),as_kana('tukau',t))
  self.assertEqual(as_kana('futari',t),as_kana('hutari',t))
  self.assertEqual(as_kana('hon',t),as_kana('honn',t))

 def test_gold_uses_raw_character_offsets(self):
  t=romaji_table(ROOT)
  p,n=prepare_alias_corpus(['customer','monitor'],['shigoto'],t)
  self.assertEqual(len(p),4)
  for x in p:
   a,b=x['gold']
   self.assertEqual(x['raw'][a:b],x['word'])
  self.assertEqual(p[0]['word'],p[1]['word'])
  self.assertNotEqual(p[0]['gold'][0],p[1]['gold'][0])
  self.assertEqual(len(n),2)

 def test_score_does_not_read_gold_or_alias_table(self):
  from h5_phonotactic_context_experiment import phonology,load_kana_tokens
  cov=phonology(load_kana_tokens(ROOT))
  e=(9,17,'customer',1.,False,.7,.4)
  v=score_boundary(e,'shigotodecustomernishimasu',cov)
  self.assertIsInstance(v,float)

 def test_calibration_protects_development_budget(self):
  from h5_phonotactic_context_experiment import phonology,load_kana_tokens
  cov=phonology(load_kana_tokens(ROOT))
  s1='shigotodecustomerga';s2='chikakudecustomerga'
  self.assertEqual(h6_calibrate([(s1,[]),(s2,[])],cov,0.0),-1e6)

 def test_boundary_f1_and_missing_spans(self):
  pos=[{'raw':'abcCUSTOMERno','gold':(3,11),'variant':0,'pair_id':0},
       {'raw':'abcMONITORno','gold':(3,10),'variant':1,'pair_id':0}]
  neg=[{'raw':'negative','pair_id':0,'variant':0}]
  fake=lambda raw: [(3,11)] if 'CUSTOMER' in raw else []
  m=metrics(pos,neg,fake)
  self.assertEqual(m['boundaries_tp'],2)
  self.assertEqual(m['boundaries_fn'],2)
  self.assertEqual(m['rank1_exact_span'],1)
  self.assertEqual(m['alias_pairs_both_exact'],0)

 def test_h6_run_on_frozen_seed(self):
  r=run(ROOT,20261010)
  self.assertEqual(r['h5']['positive_cases'],108)
  self.assertEqual(r['h6']['positive_cases'],108)
  self.assertEqual(r['h6']['negative_cases'],68)
  self.assertEqual(r['h6']['alias_pairs_total'],54)
  self.assertGreater(r['h6']['boundary_f1'],0)

if __name__=='__main__':
    unittest.main(verbosity=2)
