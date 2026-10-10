#!/usr/bin/env python3
"""H9 research: joint Japanese-romaji/English lattice decoding over raw coordinates.

IMPORTANT: Python offline proxy, not C# StreamHybrid/Mozc/Windows IME.
H8 and H7 must be available alongside this script. Evaluation cases are
previously explored synthetic H7 cases, NOT a blind natural-input benchmark.
"""
from __future__ import annotations
import argparse
import collections
import functools
import hashlib
import json
import math
import time
from pathlib import Path

from h4_error_budget_experiment import japanese_negative_streams
from h5_phonotactic_context_experiment import construct_models, phonology, load_kana_tokens, decorate, score_edges, RECIPES
from h6_boundary_alias_experiment import ALIASES, romaji_table
from h7_frozen_reference_20261010 import generate_cases, h7_calibrate, h7_select, h7_value
from h8_global_path_experiment import (BUDGET, CANDIDATES, SEEDS,
    build_development_cases, calibrate_global, edge_value, global_path, single_h7, score_case, score_development)

# Config grid is fixed in advance of evaluating H7 test cases. H7 data have
# been explored in previous studies: independence/generalization is limited.
J_GAINS=(0.02, 0.05, 0.09)
CONTEXT_BOOSTS=(0.0, 0.5, 1.0)
ISLAND_PENALTIES=(0.0, 0.20)


def japanese_edges(raw, table, reward):
    """Emit phonetic Japanese segments in raw keyboard coordinates.

    Unknown fallback ensures total path coverage; terminal unfinished phonetic
    sequences remain pending instead of being silently converted. No word
    boundaries, kanji or morphology: this is a *phonotactic* subgraph only.
    """
    n=len(raw)
    edges=[[] for _ in range(n)]
    for i in range(n):
        edges[i].append((i+1,-0.012,'Unknown'))
        for length in (3,2,1):
            if i+length<=n and raw[i:i+length] in table:
                edges[i].append((i+length,reward*length,'JapanesePhonetic'))
        if i+1<n and raw[i]==raw[i+1] and raw[i] not in 'aiueon' and raw[i].isascii() and raw[i].isalpha():
            edges[i].append((i+1,reward*.6,'JapaneseSokuon'))
        if raw[i]=='n' and (i==n-1 or raw[i+1] not in 'aiueoy'):
            edges[i].append((i+1,reward*.7,'JapaneseN'))
    return edges


def english_eligibility(raw, edges, cov, threshold, recipe, length_bonus, context_boost, per_start=4):
    """English competing edges; only strings surviving DEV-only threshold.

    Context bonus influences RANK, not the initial threshold. H7 and H8
    lexical/phonological candidate generator is left untouched.
    """
    result=[[] for _ in range(len(raw))]
    for e in edges:
        a,b=e[:2]
        val=edge_value(e,raw,cov,recipe,length_bonus)
        if val<=threshold and not e[4]:continue
        # H8 admission margin, then additional position-agnostic Japanese
        # context evidence from the H7 boundary-corrected ranker.
        profit=(max(val-threshold,0.08) if e[4] else val-threshold)
        bonus=(h7_value(e,raw,cov) - (e[3]+e[5])) if context_boost else 0.0
        profit+=context_boost*bonus
        result[a].append((b,profit,e))
    for i in range(len(raw)):
        result[i].sort(key=lambda item:(item[1],item[0]-i),reverse=True)
        result[i]=result[i][:per_start]
    return result


def joint_path(raw, edges, cov, table, recipe, threshold, length_bonus,
               japanese_gain=0.05, context_boost=0.5, island_penalty=0.2,
               per_start=4, max_islands=3):
    """Joint DP over English and Japanese phonetic edges.

    State (i,k,last_lang) covers suffix raw[i:] with <=k English islands left.
    Unlike H8, Japanese gap edges contribute nonzero path evidence. This is
    a separate hypothesis, not an implementation of the C# lattice.
    """
    n=len(raw)
    if not n:return []
    ja=japanese_edges(raw,table,japanese_gain)
    en=english_eligibility(raw,edges,cov,threshold,recipe,length_bonus,context_boost,per_start)
    # J and English edges compete on the same raw offsets. Adjacent English
    # edges remain separate lexical spans; a switch penalty is optional and
    # charged on each accepted English edge via island_penalty.
    @functools.lru_cache(maxsize=None)
    def dp(i,k):
        if i==n:return (0.,())
        best_score=-1e100;best_path=()
        for j,gain,_ in ja[i]:
            sub,path=dp(j,k)
            value=gain+sub
            if value>best_score+1e-10:
                best_score=value;best_path=path
        if k>0:
            for j,gain,_ in en[i]:
                sub,path=dp(j,k-1)
                # Charge for entering an English island; avoid forcing each
                # positive edge into the global solution.
                cost=island_penalty
                value=gain-cost+sub
                if value>best_score+1e-10:
                    best_score=value;best_path=((i,j),)+path
        return best_score,best_path
    path=list(dp(0,max_islands)[1])
    assert all(0<=a<b<=n and (i==0 or path[i-1][1]<=a)
               for i,(a,b) in enumerate(path))
    return path


def dev_choose_configs(root,ctx,cov,table,edges):
    devraw=japanese_negative_streams(ctx['jdev'],size=100)
    devneg=[(raw,edges(raw)) for raw in devraw]
    edev=[w for w in ctx['edev'] if 7<=len(w)<=24][:180]
    devcases=build_development_cases(edev)
    # Fair baseline: exactly the six configurations H8 previously tried.
    h8=[]
    for recipe,length_bonus in CANDIDATES:
        th=calibrate_global(devneg,cov,recipe,length_bonus,BUDGET)
        if th is None:continue
        def choose(raw):return global_path(raw,edges(raw),cov,recipe,th,length_bonus,4,3)
        macro,bygroup=score_development(devcases,choose)
        bad=sum(bool(choose(raw)) for raw,_ in devneg)
        if bad>math.floor(BUDGET*len(devneg)):raise AssertionError('H8 dev budget violated')
        h8.append(dict(recipe=recipe,length_bonus=length_bonus,threshold=th,macro=macro,bad=bad,bygroup=bygroup))
    best8=max(h8,key=lambda x:(x['macro'],-x['bad'],-x['length_bonus'],x['recipe']))

    # Stage 1: use H8-selected base features and threshold for H9. This
    # avoids choosing many degrees of freedom by repeatedly reading test data.
    recipe=best8['recipe'];length_bonus=best8['length_bonus'];threshold=best8['threshold']
    configs=[]
    for japanese_gain in J_GAINS:
        for context_boost in CONTEXT_BOOSTS:
            for island_penalty in ISLAND_PENALTIES:
                def choose(raw):
                    return joint_path(raw,edges(raw),cov,table,recipe,threshold,length_bonus,
                                      japanese_gain,context_boost,island_penalty)
                macro,bygroup=score_development(devcases,choose)
                bad=sum(bool(choose(raw)) for raw,_ in devneg)
                if bad>math.floor(BUDGET*len(devneg)):
                    continue
                configs.append(dict(recipe=recipe,length_bonus=length_bonus,threshold=threshold,
                    japanese_gain=japanese_gain,context_boost=context_boost,island_penalty=island_penalty,
                    dev_macro=macro,dev_false=bad,dev_by_group=bygroup))
    if not configs:
        raise ValueError('No H9 configuration meets the development false-candidate budget')
    selected=max(configs,key=lambda x:(x['dev_macro'],-x['dev_false'],
                    -x['context_boost'],-x['japanese_gain'],-x['island_penalty']))
    return best8,selected,configs,devneg


def result_for_seed(root,cases,seed,record_examples=8):
    ctx=construct_models(root,seed)
    table=romaji_table(root)
    cov=phonology(load_kana_tokens(root))
    @functools.lru_cache(maxsize=40000)
    def edges(raw):return decorate(raw,score_edges(raw,ctx['diff']),cov)
    best8,config,search,negdev=dev_choose_configs(root,ctx,cov,table,edges)
    h7th=h7_calibrate(negdev,cov,BUDGET)
    assert h7th is not None
    counters={name:{g:collections.Counter() for g in sorted(set(c['group'] for c in cases))}
              for name in ('H7_single','H8_global','H9_joint')}
    errors={name:collections.defaultdict(list) for name in counters}
    alias={name:collections.defaultdict(list) for name in counters}
    for case in cases:
        raw=case['raw'];es=edges(raw)
        # A more useful oracle than the topline: do ALL gold spans exist in
        # initial candidates, regardless of eventual rank/path selection?
        goldset={tuple(x) for x in case['gold']}
        present={tuple(e[:2]) for e in es}
        controls={
          'H7_single':single_h7(raw,es,cov,h7th),
          'H8_global':global_path(raw,es,cov,best8['recipe'],best8['threshold'],best8['length_bonus'],4,3),
          'H9_joint':joint_path(raw,es,cov,table,config['recipe'],config['threshold'],config['length_bonus'],
                config['japanese_gain'],config['context_boost'],config['island_penalty'],4,3),
        }
        for name,spans in controls.items():
            m=score_case(case,spans)
            c=counters[name][case['group']]
            c.update(n=1,exact=int(m['exact']),gold_spans=m['gold_spans'],
              raw_gold_spans=len(present&goldset),all_gold_raw=int(goldset<=present),
              found_spans=m['hit_spans'],predicted_spans=m['pred_spans'],
              false_cases=int(m['false_case']),tp=m['tp'],fp=m['fp'],fn=m['fn'])
            if case.get('pair') is not None:alias[name][case['pair']].append(bool(m['exact']))
            if not m['exact'] and len(errors[name][case['group']])<record_examples:
                errors[name][case['group']].append({'raw':raw,'gold':case['gold'],'pred':spans,
                     'all_gold_raw':bool(goldset<=present)})
    result={}
    for name,groups in counters.items():
        groupdata={}
        for group,ct in groups.items():
            item=dict(ct)
            item['exact_rate']=round(item.get('exact',0)/item['n'],5)
            item['span_recall']=(round(item.get('found_spans',0)/item['gold_spans'],5)
                          if item['gold_spans'] else None)
            tp,fp,fn=(item.get(k,0) for k in ('tp','fp','fn'))
            item['language_boundary_f1']=round(2*tp/(2*tp+fp+fn),5) if 2*tp+fp+fn else None
            groupdata[group]=item
        pairvals=[v for v in alias[name].values() if len(v)==2]
        result[name]={'by_group':groupdata,'alias_pairs':{'n':len(pairvals),
             'both_exact':sum(all(v) for v in pairvals)},'examples':dict(errors[name])}
    return {'seed':seed,'h8_selected_on_dev':best8,'h9_selected_on_dev':config,
           'h9_config_feasible_count':len(search),'results':result,
           'heldout_english_types':len(ctx['oov']),'heldout_japanese_types':len(ctx['jtest']+ctx['phon'])}


def run(root,output,seeds=SEEDS,limit=None):
    base=construct_models(root,seeds[0]);table=romaji_table(root)
    cases=generate_cases(base['oov'],base['jtest']+base['phon'],table,ALIASES)
    if limit is not None:cases=cases[:limit]
    records=[];start=time.perf_counter()
    for seed in seeds:
        print(f'SEED {seed} start {len(cases)}',flush=True)
        result=result_for_seed(root,cases,seed)
        records.append(result)
        print(f'SEED {seed} done {time.perf_counter()-start:.1f}s config={result["h9_selected_on_dev"]["japanese_gain"],result["h9_selected_on_dev"]["context_boost"],result["h9_selected_on_dev"]["island_penalty"]}',flush=True)
    obj={'study':'H9 joint Japanese-phonetic and English lattice scoring',
         'layer':'offline Python; NOT actual C# or Windows IME',
         'corpus':'Previously examined synthetic H7 13-category generator; NOT unseen natural language',
         'seeds':list(seeds),'cases_per_seed':len(cases),
         'unique_raw_per_seed':len({c['raw'] for c in cases}),
         'group_counts':dict(collections.Counter(x['group'] for x in cases)),
         'metrics':'Exact full English span-set, span recall, internal language-switch boundary F1, negative cases',
         'config_selection':'H8 recipe/length chosen on English dev; H9 18 fixed configs evaluated on English/Japanese dev only',
         'records':records,
         'limitations':['Train/dev/test words grouped; same heldout English types reused across seeds',
          'Artificial test templates were seen in prior H6-H8 research: NOT blind evaluation',
          'Japanese paths are local phonetic tokens, not grammar, dictionary or Mozc output',
          'Raw English candidate scanner unchanged; short words and symbols remain unsupported',
          'No C# IME, per-keystroke latency or user acceptance evaluated']}
    output.parent.mkdir(parents=True,exist_ok=True)
    content=json.dumps(obj,ensure_ascii=False,sort_keys=True,indent=2)+'\n'
    output.write_text(content,encoding='utf-8')
    sha=hashlib.sha256(content.encode()).hexdigest()
    print(json.dumps({'path':str(output),'sha256':sha,'elapsed':round(time.perf_counter()-start,2)},ensure_ascii=False))
    return obj

if __name__=='__main__':
    ap=argparse.ArgumentParser()
    ap.add_argument('--root',type=Path,default=Path('.'))
    ap.add_argument('--output',type=Path,default=Path('h9-full.json'))
    ap.add_argument('--seeds',nargs='+',type=int,default=SEEDS)
    ap.add_argument('--limit',type=int)
    args=ap.parse_args()
    run(args.root,args.output,args.seeds,args.limit)
