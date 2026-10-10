#!/usr/bin/env python3
"""Additional H9 diagnostic: out-of-template robustness, candidate survivability,
Japanese-only false positives, 1-4 English islands and latency/length.

All gold labels are synthetic and known before scoring; no test labels are used
for model/threshold tuning. Python offline proxy; not C# or actual IME.
"""
import argparse
import collections
import functools
import hashlib
import json
import math
import os
import platform
import random
import statistics
import sys
import time
from pathlib import Path

from h4_error_budget_experiment import japanese_negative_streams
from h5_phonotactic_context_experiment import construct_models, phonology, load_kana_tokens, decorate, score_edges, RECIPES
from h6_boundary_alias_experiment import romaji_table, ALIASES
from h7_frozen_reference_20261010 import h7_calibrate
from h8_global_path_experiment import global_path, single_h7, score_case
from h9_joint_lattice_experiment import dev_choose_configs, joint_path, english_eligibility, japanese_edges

SEEDS=(20261010,20261011,20261012,20261013,20261014)
PRE=('korekara','kinouha','ashitamo','soredeha','kokode','watashiga','dokoni','bokuno','koremo','koreto','totemo','sorega')
POST=('nidesu','gasuki','woerabu','wakaru','toiu','matane','dekiru','ninaru','sonomama','nodakedo','gaatta','datta')
SHORT=('ime','api','ui','cpu','gpu','js','css','html','git','node','tokyo','go','rust','ai','ram','os')


def cases_for_seed(oov, ja):
    out=[]
    def add(group, chunks, pair=None):
        raw=''.join(s for s,_ in chunks)
        at=0; gold=[]; last=None; switches=[]
        for s,kind in chunks:
            if s and last in ('E','J') and kind in ('E','J') and last!=kind:
                switches.append(at)
            if kind=='E' and s:
                gold.append([at,at+len(s)])
            at+=len(s)
            if s:last=kind
        assert all(raw[a:b] for a,b in gold)
        out.append({'case_id':len(out),'group':group,'raw':raw,'gold':gold,'switches':switches,'pair_id':pair})
    for i,w in enumerate(oov):
        for j in range(2):
            pre=PRE[(i+j*5)%len(PRE)];post=POST[(i*3+j)%len(POST)]
            add('start_new_context',[(w,'E'),(post,'J')])
            add('middle_new_context',[(pre,'J'),(w,'E'),(post,'J')])
            add('end_new_context',[(pre,'J'),(w,'E')])
        add('english_only',[(w,'E')])
        a=oov[(i*13+5)%len(oov)]
        b=oov[(i*19+11)%len(oov)]
        c=oov[(i*23+17)%len(oov)]
        pre=PRE[i%len(PRE)];post=POST[(i+3)%len(POST)]
        add('two_islands_new',[(pre,'J'),(w,'E'),('no','J'),(a,'E'),(post,'J')])
        add('three_islands_new',[(pre,'J'),(w,'E'),('no','J'),(a,'E'),('de','J'),(b,'E'),(post,'J')])
        add('four_islands_cap',[(pre,'J'),(w,'E'),('no','J'),(a,'E'),('de','J'),(b,'E'),('ga','J'),(c,'E'),(post,'J')])
        v0,v1,q0,q1=ALIASES[i%len(ALIASES)]
        add('alias_new_context',[(v0,'J'),(w,'E'),(q0,'J')],pair=f'alias-{i}')
        add('alias_new_context',[(v1,'J'),(w,'E'),(q1,'J')],pair=f'alias-{i}')
        add('upper_and_title',[(pre,'J'),(w.upper() if i%2 else w.title(),'E'),(post,'J')])
        add('symbol_new_context',[(pre,'J'),(w,'E'),(('.', '#','@','/')[i%4],'S'),(post,'J')])
    for i,w in enumerate(ja):
        for j in range(9):
            a=PRE[(i+j)%len(PRE)];b=POST[(i+j*3)%len(POST)]
            k=ja[(i*7+j*11+1)%len(ja)]
            raw=[(a,'J'),(w,'J'),(b,'J'),(k,'J'),('no','J')]
            add('japanese_only_shift',raw)
    for j,w in enumerate(SHORT):
        for pre,post in ((PRE[j%len(PRE)],POST[j%len(POST)]),('',POST[(j+2)%len(POST)])):
            add('short_intent_english',[(pre,'J'),(w,'E'),(post,'J')])
    return out


def stage(raw,es,ctx,cov,table,c8,c9):
    # Eligible English edge set: same H9 threshold and per-start top-4
    en=english_eligibility(raw,es,cov,c9['threshold'],c9['recipe'],c9['length_bonus'],c9['context_boost'],4)
    return {(i,end) for i,arr in enumerate(en) for end,profit,e in arr}


def run_seed(root,seed,cases,examples_limit=7):
    ctx=construct_models(root,seed)
    cov=phonology(load_kana_tokens(root));table=romaji_table(root)
    @functools.lru_cache(maxsize=30000)
    def getedges(raw):return decorate(raw,score_edges(raw,ctx['diff']),cov)
    h8,h9,configs,devneg=dev_choose_configs(root,ctx,cov,table,getedges)
    h7th=h7_calibrate(devneg,cov,.05)
    configs_out={'h8':{'recipe':h8['recipe'],'length_bonus':h8['length_bonus'],'threshold':round(h8['threshold'],6)},
      'h9':{k:round(h9[k],6) if isinstance(h9[k],float) else h9[k] for k in ('recipe','length_bonus','threshold','japanese_gain','context_boost','island_penalty')},
      'h9_dev_false':h9['dev_false'],'dev_neg_n':len(devneg),'h9_feasible_configs':len(configs)}
    groups=collections.defaultdict(lambda:{m:collections.Counter() for m in ('H7','H8','H9')})
    misses=collections.defaultdict(list)
    pairs=collections.defaultdict(lambda:collections.defaultdict(list))
    for c in cases:
        raw=c['raw'];es=getedges(raw);gold={tuple(x) for x in c['gold']}
        present={e[:2] for e in es}
        admitted=stage(raw,es,ctx,cov,table,h8,h9)
        ph7=single_h7(raw,es,cov,h7th)
        ph8=global_path(raw,es,cov,h8['recipe'],h8['threshold'],h8['length_bonus'],4,3)
        ph9=joint_path(raw,es,cov,table,h9['recipe'],h9['threshold'],h9['length_bonus'],
                      h9['japanese_gain'],h9['context_boost'],h9['island_penalty'],4,3)
        preds={'H7':ph7,'H8':ph8,'H9':ph9}
        for name,pred in preds.items():
            s=score_case(c,pred)
            stat=groups[c['group']][name]
            stat.update(n=1,exact=int(s['exact']),span_hits=s['hit_spans'],gold_spans=s['gold_spans'],
                        tp=s['tp'],fp=s['fp'],fn=s['fn'],predicted_spans=s['pred_spans'],
                        false_case=int(s['false_case']))
            if name=='H9':
                st={'raw_missing':len(gold-present), 'admission_missing':len(gold-admitted),
                    'raw_complete':int(gold<=present),'admitted_complete':int(gold<=admitted),
                    'rank_wrong_despite_admission':int(gold<=admitted and not s['exact'])}
                stat.update(**st)
                if not s['exact'] and len(misses[c['group']])<examples_limit:
                    misses[c['group']].append({'raw':raw,'gold':c['gold'],'H8':ph8,'H9':ph9,
                      'raw_all_gold':bool(gold<=present),'admitted_all_gold':bool(gold<=admitted)})
            if c['pair_id']:pairs[name][c['pair_id']].append(bool(s['exact']))
    result={}
    for group,methods in groups.items():
        result[group]={}
        for name,ctr in methods.items():
            d=dict(ctr);n=d['n']; tp,fp,fn=(d.get(k,0) for k in ('tp','fp','fn'))
            d['exact_rate']=round(d.get('exact',0)/n,5)
            d['switch_f1']=round(2*tp/(2*tp+fp+fn),5) if 2*tp+fp+fn else None
            result[group][name]=d
    pairing={name:{'pairs':len(vals),'both_exact':sum(len(v)==2 and all(v) for v in vals.values()),
                  'one_exact':sum(len(v)==2 and sum(v)==1 for v in vals.values())} for name,vals in pairs.items()}
    # performance compare same cached candidate edges, repeated path selection only
    perf={}
    for n in (40,80,160,320):
        raw=('shigotodecustomerga'* ((n//18)+3))[:n]
        es=getedges(raw)
        fn8=lambda:global_path(raw,es,cov,h8['recipe'],h8['threshold'],h8['length_bonus'],4,3)
        fn9=lambda:joint_path(raw,es,cov,table,h9['recipe'],h9['threshold'],h9['length_bonus'],
                    h9['japanese_gain'],h9['context_boost'],h9['island_penalty'],4,3)
        d={}
        for name,fun in (('H8',fn8),('H9',fn9)):
            try:
                for _ in range(3):fun()
                timings=[]
                for _ in range(12):
                    t=time.perf_counter_ns();fun();timings.append((time.perf_counter_ns()-t)/1e6)
                d[name]={'median_ms':round(statistics.median(timings),3),
                         'p95_ms':round(sorted(timings)[math.ceil(.95*len(timings))-1],3),
                         'max_ms':round(max(timings),3)}
            except Exception as exc:
                d[name]={'error':type(exc).__name__,'message':str(exc)[:200]}
        perf[str(n)]=d
    # recursion depth structural probe with few/no English edges; MUST NOT claim IME latency.
    recursion=[]
    for n in (400,800,1200):
        raw='ka'*(n//2)
        try:
            joint_path(raw,[],cov,table,h9['recipe'],h9['threshold'],h9['length_bonus'],
                       h9['japanese_gain'],h9['context_boost'],h9['island_penalty'],4,3)
            recursion.append({'length':n,'status':'pass'})
        except Exception as exc:
            recursion.append({'length':n,'status':type(exc).__name__})
    return {'seed':seed,'config':configs_out,'groups':result,'alias':pairing,'examples':dict(misses),
            'perf':perf,'recursion_probe':recursion}


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--root',type=Path,default=Path('.'))
    ap.add_argument('--output',type=Path,default=Path('h9-extra-results.json'))
    ap.add_argument('--seeds',type=int,nargs='+',default=SEEDS)
    a=ap.parse_args()
    seed0=construct_models(a.root,a.seeds[0]); cases=cases_for_seed(seed0['oov'],seed0['jtest']+seed0['phon'])
    obj={'study':'H9 additional out-of-template diagnostics', 'scope':'offline Python joint lattice only',
      'corpus':'synthetic new contexts, same OOV test word types as H1-H9, exploratory not blinded',
      'created_utc':None,'python':sys.version.split()[0],'platform':platform.platform(),
      'seeds':a.seeds,'cases_per_seed':len(cases),'unique_raw_per_seed':len({x['raw'] for x in cases}),
      'group_counts':dict(collections.Counter(x['group'] for x in cases)),
      'records':[]}
    for seed in a.seeds:
        start=time.perf_counter();r=run_seed(a.root,seed,cases)
        obj['records'].append(r)
        print(f'seed {seed} finished {time.perf_counter()-start:.1f}s',flush=True)
    a.output.parent.mkdir(parents=True,exist_ok=True)
    content=json.dumps(obj,ensure_ascii=False,indent=2,sort_keys=True)+'\n';a.output.write_text(content,encoding='utf-8')
    print('file',a.output,'sha256',hashlib.sha256(content.encode()).hexdigest(),'cases',len(cases),'unique',obj['unique_raw_per_seed'])

if __name__=='__main__':main()
