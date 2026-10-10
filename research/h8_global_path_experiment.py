#!/usr/bin/env python3
"""H8: global multi-English-span path search, with development-only configuration.

Offline Python candidate-only research, *not* C# StreamHybrid/IME. H7's
stratified synthetic cases are used as a frozen exploratory benchmark; these
are not previously unseen natural language utterances. Results include full
English span-set accuracy and Japanese-English language switch boundaries.
"""
from __future__ import annotations
import argparse
import collections
import functools
import hashlib
import json
import math
import statistics
import time
from pathlib import Path

from h4_error_budget_experiment import japanese_negative_streams
from h5_phonotactic_context_experiment import (construct_models, phonology,
    load_kana_tokens, decorate, score_edges, RECIPES, score, selected, calibrate)
from h6_boundary_alias_experiment import ALIASES, romaji_table
from h7_frozen_reference_20261010 import generate_cases, h7_value, h7_select, h7_calibrate

SEEDS=(20261010,20261011,20261012,20261013,20261014)
BUDGET=.05
CANDIDATES=(('phonetic_only',0.00), ('phonetic_only',0.04),
            ('phonetic_only',0.08), ('h7_context',0.00),
            ('h7_context',0.04), ('h7_context',0.08))
DEV_GROUPS=('start','middle','end','english_only','two_islands')


def build_development_cases(english_words):
    """Only known dev English words and development Japanese text fragments.

    Never use held-out 54 OOV words, H7 test results, or their labels to
    select H8 configuration. Gold offsets reflect raw concatenation.
    """
    pre=('oreha','shigotode','chikakude','sorekara','watashino')
    post=('ga','no','nishimasu','nitsukau','toiu')
    cases=[]
    def add(group,chunks):
        raw=''.join(s for s,l in chunks)
        pos=0;spans=[]
        for token,kind in chunks:
            if kind=='E':spans.append((pos,pos+len(token)))
            pos+=len(token)
        assert all(raw[a:b] and 0<=a<b<=len(raw) for a,b in spans)
        cases.append({'group':group,'raw':raw,'gold':spans})
    for i,w in enumerate(english_words):
        p=pre[i%len(pre)]; q=post[i%len(post)]
        add('start',[(w,'E'),(q,'J')])
        add('middle',[(p,'J'),(w,'E'),(q,'J')])
        add('end',[(p,'J'),(w,'E')])
        add('english_only',[(w,'E')])
        if i%3==0:
            other=english_words[(i*17+1)%len(english_words)]
            add('two_islands',[(p,'J'),(w,'E'),('ga','J'),(other,'E'),(q,'J')])
    return cases


def edge_value(edge,raw,cov,recipe,length_bonus):
    base=(score(edge,RECIPES['char_phonotactic']) if recipe=='phonetic_only'
          else h7_value(edge,raw,cov))
    return base+length_bonus*(edge[1]-edge[0])


def calibrate_global(dev_neg,cov,recipe,length_bonus,budget=BUDGET):
    """Calibrate per-edge gate using ONLY Japanese dev cases.

    Threshold controls whether *any* island would be selected. Consequently,
    absent other restrictions, global path is bounded to the same empirical
    dev false-case budget. Fixed morphology overrides retained, exactly as H7.
    """
    limit=math.floor(budget*len(dev_neg))
    unavoidable=sum(any(e[4] for e in edges) for raw,edges in dev_neg)
    if unavoidable>limit:
        return None
    margins=sorted((max((edge_value(e,raw,cov,recipe,length_bonus)
                         for e in edges),default=-1e9)
                    for raw,edges in dev_neg if not any(e[4] for e in edges)),reverse=True)
    remaining=limit-unavoidable
    th=margins[remaining] if remaining<len(margins) else -1e6
    return th


def global_path(raw,edges,cov,recipe,threshold,length_bonus=0.,per_start=4,max_islands=3):
    """Non-overlapping interval DP over raw ASCII positions.

    Japanese/unknown gaps cost zero. Accepted English span contributes its
    excess score above a dev-calibrated threshold, or a small fixed margin
    when admitted by morphology. Top-N limit is PER START, not global;
    therefore multiple English islands can appear in one path. The DP does
    not perform Japanese lexical segmentation or handle symbols structurally.
    """
    n=len(raw)
    starts=[[] for _ in range(n+1)]
    for edge in edges:
        a,b=edge[:2]
        if not (0<=a<b<=n):raise ValueError('invalid lattice coordinates')
        val=edge_value(edge,raw,cov,recipe,length_bonus)
        if val<=threshold and not edge[4]:continue
        profit=max(val-threshold,0.1) if edge[4] else val-threshold
        starts[a].append((profit,a,b))
    for at in range(n):
        starts[at].sort(key=lambda x:(x[0],x[2]-x[1]),reverse=True)
        if per_start is not None:starts[at]=starts[at][:per_start]
    # dp[i][k]: maximal score from i onwards, using <=k English islands.
    scores=[[0.]*(max_islands+1) for _ in range(n+1)]
    traces=[[()]*(max_islands+1) for _ in range(n+1)]
    for i in range(n-1,-1,-1):
        for k in range(max_islands+1):
            best=scores[i+1][k];path=traces[i+1][k]
            if k:
                for profit,a,b in starts[i]:
                    candidate=profit+scores[b][k-1]
                    # Tiny deterministic tie-break prefers fewer spans, then earlier.
                    if candidate>best+1e-10:
                        best=candidate
                        path=((a,b),)+traces[b][k-1]
            scores[i][k]=best;traces[i][k]=path
    chosen=list(traces[0][max_islands])
    assert all(a<b and (i==0 or chosen[i-1][1]<=a) for i,(a,b) in enumerate(chosen))
    return chosen


def single_h7(raw,edges,cov,threshold):
    out=h7_select(edges,raw,cov,threshold,4)
    return [tuple(out[0][:2])] if out else []


def score_case(case,chosen):
    gold=[tuple(z) for z in case['gold']]
    if len(chosen)!=len(set(chosen)):raise AssertionError('duplicate span')
    exact=tuple(chosen)==tuple(gold)
    hit=len(set(gold)&set(chosen))
    # Adjacent E pieces have *no* language-switch boundary between them; the
    # two neighboring spans still matter for exact lexical span-set matching.
    switch=set(case.get('switches',[]))
    points=set()
    for a,b in chosen:
        if 0<a<len(case['raw']):points.add(a)
        if 0<b<len(case['raw']):points.add(b)
    good=lambda p:0<p<len(case['raw']) and case['raw'][p-1:p+1].isalpha()
    switch={p for p in switch if good(p)}
    points={p for p in points if good(p)}
    return {'exact':exact,'gold_spans':len(gold),'hit_spans':hit,
            'pred_spans':len(chosen),'false_case':not gold and bool(chosen),
            'tp':len(switch&points),'fp':len(points-switch),'fn':len(switch-points)}


def score_development(cases,choose):
    stats=collections.defaultdict(lambda:[0,0])
    for case in cases:
        exact=choose(case['raw'])==[tuple(g) for g in case['gold']]
        stats[case['group']][0]+=int(exact)
        stats[case['group']][1]+=1
    # Equal weight for start, middle, end, English-only, and two-island dev.
    macro=sum(a/b for a,b in (stats[k] for k in DEV_GROUPS))/len(DEV_GROUPS)
    return macro,{k:{'correct':a,'total':b} for k,(a,b) in stats.items()}


def result_for_seed(root,cases,seed,include_failures=12):
    ctx=construct_models(root,seed)
    cov=phonology(load_kana_tokens(root))
    @functools.lru_cache(maxsize=40000)
    def edges(raw):return decorate(raw,score_edges(raw,ctx['diff']),cov)
    devraw=japanese_negative_streams(ctx['jdev'],size=100)
    devneg=[(raw,edges(raw)) for raw in devraw]
    h7th=h7_calibrate(devneg,cov,BUDGET)
    if h7th is None:raise ValueError('H7 baseline not calibratable')
    # Development English candidates are from the *training split's dev set*;
    # the 54 held-out evaluation OOV word types never influence parameter choice.
    edev=[w for w in ctx['edev'] if 7<=len(w)<=24][:180]
    devcases=build_development_cases(edev)
    candidate_scores=[]
    for recipe,bonus in CANDIDATES:
        th=calibrate_global(devneg,cov,recipe,bonus)
        if th is None:continue
        def choose(raw):return global_path(raw,edges(raw),cov,recipe,th,bonus,4,3)
        macro,dev=score_development(devcases,choose)
        dev_false=sum(bool(choose(raw)) for raw,_ in devneg)
        if dev_false>math.floor(BUDGET*len(devneg)):
            raise AssertionError('development FPR exceeds budget')
        candidate_scores.append((macro, -dev_false, -bonus, recipe,bonus,th,dev))
    if not candidate_scores:raise ValueError('No feasible calibrated H8')
    macro, negfpr, _,recipe,bonus,th,dev=max(candidate_scores)
    config={'recipe':recipe,'length_bonus':bonus,'threshold':round(th,6),
      'dev_macro_exact':round(macro,5),'dev_negative_cases':len(devneg),
      'dev_false_cases':-negfpr,'dev_exact_by_group':dev}
    results={m:{g:collections.Counter() for g in sorted(set(c['group'] for c in cases))}
             for m in ('H7_single','H8_global')}
    alias={m:collections.defaultdict(list) for m in results}
    failures={m:collections.defaultdict(list) for m in results}
    for case in cases:
        raw=case['raw'];es=edges(raw)
        candidate_ranges={tuple(e[:2]) for e in es}
        gold_ranges={tuple(g) for g in case['gold']}
        raw_gold_hits=len(candidate_ranges & gold_ranges)
        outputs={'H7_single':single_h7(raw,es,cov,h7th),
                 'H8_global':global_path(raw,es,cov,recipe,th,bonus,4,3)}
        for method,ch in outputs.items():
            metrics=score_case(case,ch)
            counter=results[method][case['group']]
            counter.update(n=1,exact=int(metrics['exact']),gold_spans=metrics['gold_spans'],
              gold_in_raw_candidates=raw_gold_hits,all_gold_in_raw_candidates=int(raw_gold_hits==len(gold_ranges)),
              hit_spans=metrics['hit_spans'],selected_spans=metrics['pred_spans'],
              false_cases=int(metrics['false_case']),tp=metrics['tp'],fp=metrics['fp'],fn=metrics['fn'])
            if case.get('pair') is not None:alias[method][case['pair']].append(bool(metrics['exact']))
            if not metrics['exact'] and len(failures[method][case['group']])<include_failures:
                failures[method][case['group']].append({'raw':raw,'gold':case['gold'],'pred':ch})
    outcome={}
    for method,by_group in results.items():
        grouped={}
        for group,count in by_group.items():
            stat=dict(count)
            tp,fp,fn=(stat.get(k,0) for k in ('tp','fp','fn'))
            stat['exact_rate']=round(stat.get('exact',0)/stat['n'],5)
            stat['span_recall']=round(stat.get('hit_spans',0)/stat.get('gold_spans',1),5) if stat.get('gold_spans',0) else None
            stat['switch_boundary_micro_f1']=round(2*tp/(2*tp+fp+fn),5) if (2*tp+fp+fn) else None
            grouped[group]=stat
        pairs=[vals for vals in alias[method].values() if len(vals)==2]
        outcome[method]={'groups':grouped,'alias_pairs':{'n':len(pairs),'both_exact':sum(all(v) for v in pairs)},
                        'failures':dict(failures[method])}
    return {'seed':seed,'selected_h8_config':config,'h7_baseline_threshold':round(h7th,6),
      'candidate_configurations':[{ 'dev_macro_exact':round(s[0],5),'dev_false_cases':-s[1],
        'recipe':s[3],'length_bonus':s[4],'threshold':round(s[5],5)} for s in candidate_scores],
      'results':outcome,'model_development_english_types':len(edev),
      'test_english_types':len(ctx['oov']),'japanese_development_types':len(ctx['jdev']),
      'japanese_test_types':len(ctx['jtest']+ctx['phon'])}


def run(root,output,seeds=SEEDS,limit=None):
    context=construct_models(root,seeds[0]);table=romaji_table(root)
    cases=generate_cases(context['oov'],context['jtest']+context['phon'],table,ALIASES)
    if limit is not None:cases=cases[:limit]
    t0=time.monotonic();records=[]
    for seed in seeds:
        print(f'SEED START {seed}: {len(cases)} cases',flush=True)
        result=result_for_seed(root,cases,seed)
        records.append(result)
        print(f'SEED END {seed}: {time.monotonic()-t0:.2f}s, H8 config={result["selected_h8_config"]}',flush=True)
    obj={'study':'H8 global multi-English span path search','evaluation_layer':'A: offline Python candidate search; not C# or IME',
      'reference':'H7 stratified case generator; same synthetic word types, reused five seeds',
      'metric':'exact labeled English span-set and internal E/J switches; no Japanese lexical word boundaries',
      'cases_per_seed':len(cases),'unique_raw_per_seed':len(set(c['raw'] for c in cases)),
      'seeds':list(seeds),'budget_dev_false_case_rate':BUDGET,
      'by_group':dict(collections.Counter(c['group'] for c in cases)),
      'records':records,'limits':['Current H4 scanner rejects short/uppercase/symbol English in many cases',
        'DP uses Japanese gaps of zero score, not true Japanese/Mozc paths',
        'Held-out OOV list and H7 synthetic templates have been reviewed previously; not a fresh blind corpus',
        'Threshold/hyperparameters are calibrated on development data only',
        'All seeds reuse English OOV types; counts cannot be treated as independent user messages']}
    output.parent.mkdir(parents=True,exist_ok=True)
    data=json.dumps(obj,ensure_ascii=False,indent=2)+'\n'
    output.write_text(data,encoding='utf-8')
    print(json.dumps({'output':str(output),'sha256':hashlib.sha256(data.encode()).hexdigest(),
       'elapsed_seconds':round(time.monotonic()-t0,2),'cases_per_seed':len(cases),
       'selected':[r['selected_h8_config'] for r in records]},ensure_ascii=False))
    return obj

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path('.'))
    parser.add_argument('--output',type=Path,default=Path('h8-output.json'))
    parser.add_argument('--seeds',type=int,nargs='+',default=SEEDS)
    parser.add_argument('--limit',type=int)
    args=parser.parse_args()
    run(args.root,args.output,args.seeds,args.limit)
