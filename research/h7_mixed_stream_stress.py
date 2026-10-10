#!/usr/bin/env python3
"""H7 mixed-input stress: H5/H6 ranker audit + exploratory missing-side H7.
Python candidate-level research, NOT C# StreamHybrid/Windows IME accuracy.
"""
from __future__ import annotations
import argparse,collections,functools,json,math,random,statistics,time
from pathlib import Path

from h4_error_budget_experiment import japanese_negative_streams
from h5_phonotactic_context_experiment import (construct_models,phonology,load_kana_tokens,
    decorate,score_edges,RECIPES,selected,calibrate,score)
from h6_boundary_alias_experiment import (ALIASES,as_kana,romaji_table,
    score_boundary,h6_select,h6_calibrate)

GROUPS=('start','middle','end','english_only','two_islands','adjacent_english',
'romanization_pairs','short_literal','symbols','case_variants','no_english',
'nonsense_literal','inside_mora')

def generate_cases(oov,japanese,table,aliases):
    cases=[]
    def add(group,chunks,pair=None,tags=()):
        raw=''.join(t for t,l in chunks)
        if len(raw)>108:return
        spans=[];switches=[];at=0;prev=None
        for t,l in chunks:
            if t and prev is not None and prev!=l and {prev,l}=={'J','E'}:switches.append(at)
            if l=='E' and t:spans.append((at,at+len(t)))
            if t:prev=l
            at+=len(t)
        cases.append(dict(id=len(cases),group=group,raw=raw,
                          gold=[list(z) for z in spans],switches=switches,pair=pair,tags=list(tags)))
    pre=['','oreha','watashino','kyouha','shigotode','sigotode','chikakude',
       'tikakude','shashinde','syasinde','ashitano','gakuseiha','sorekara']
    post=['','ha','ga','no','ni','de','to','wo','nishimasu','nisimasu','nitsukau','nitukau','toiu']
    for i,w in enumerate(oov):
        for j,suffix in enumerate(post[1:]):add('start',[(w,'E'),(suffix,'J')],tags=(str(j),))
        for j,prefix in enumerate(pre[1:]):add('end',[(prefix,'J'),(w,'E')],tags=(str(j),))
        for j in range(14):
            add('middle',[(pre[1+(i+j)%12],'J'),(w,'E'),(post[1+(i*3+j)%12],'J')],tags=(str(j),))
        add('english_only',[(w,'E')])
    for i,w in enumerate(oov):
        other=oov[(i*7+11)%len(oov)]
        for j in range(6):
            prefix=pre[1+(i+j)%12];suffix=post[1+(i+j*2)%12]
            between=('ga','no','ni','de','ha','to')[j]
            add('two_islands',[(prefix,'J'),(w,'E'),(between,'J'),(other,'E'),(suffix,'J')])
            add('adjacent_english',[(prefix,'J'),(w,'E'),(other,'E'),(suffix,'J')])
    for i,w in enumerate(oov):
        for j,(p0,p1,q0,q1) in enumerate(aliases):
            assert as_kana(p0,table)==as_kana(p1,table) is not None
            assert as_kana(q0,table)==as_kana(q1,table) is not None
            add('romanization_pairs',[(p0,'J'),(w,'E'),(q0,'J')],pair=f'{i}:{j}',tags=('style0',))
            add('romanization_pairs',[(p1,'J'),(w,'E'),(q1,'J')],pair=f'{i}:{j}',tags=('style1',))
    short=['ime','api','ui','ai','os','ram','cpu','gpu','js','css','html','git','node','tokyo','go','rust']
    for w in short:
        for j in range(18):
            mode=j%3
            p=pre[1+(j+i)%12];q=post[1+(j*3+i)%12]
            chunks=[(w,'E'),(q,'J')] if mode==0 else (
               [(p,'J'),(w,'E')] if mode==1 else [(p,'J'),(w,'E'),(q,'J')])
            add('short_literal',chunks,tags=(w,))
    for i,w in enumerate(oov):
        for transform in (str.upper,str.capitalize):
            val=transform(w)
            for j in range(2):
                add('case_variants',[(pre[1+(i+j)%12],'J'),(val,'E'),(post[1+(i+j)%12],'J')],tags=(val,))
    for i,w in enumerate(oov):
        for sym in ('.','-','_','/','@','#'):
            add('symbols',[(pre[1+i%12],'J'),(w,'E'),(sym,'S'),('no','J')],tags=(sym,))
    for i,w in enumerate(japanese):
        for j in range(28):
            p=pre[1+(i+j)%12];q=post[1+(j*3+i)%12]
            chunks=[(p,'J'),(w,'J'),(q,'J')]
            if j%2==1:chunks.extend([(japanese[(i*5+j)%len(japanese)],'J'),('ni','J')])
            add('no_english',chunks)
    nonsense=('qzxvbnmk','zxqvqqtv','abcdefghij','ababababab','rtrtrtrtrt',
      'strstrstrst','xxyzzzyyx','mmnnvvbbcc','ueueueueu','ababzababz','xyzzxyzzxy','qwertyuiop')
    for i,w in enumerate(nonsense):
        for j in range(24):
            p=pre[1+(i+j)%12];q=post[1+(j+i*2)%12]
            chunks=[(w,'E'),(q,'J')] if j%3==0 else (
               [(p,'J'),(w,'E')] if j%3==1 else [(p,'J'),(w,'E'),(q,'J')])
            add('nonsense_literal',chunks,tags=(w,))
    for i,w in enumerate(oov):
        for j,part in enumerate(('sh','ch','ts','sy')):
            add('inside_mora',[(pre[1+i%12]+part,'J'),(w,'E'),('i'+post[1+(i+j)%12],'J')])
    assert len(cases)==5686
    return cases

def structural_fuzz(table,seed,count):
    rng=random.Random(seed)
    tokens=list(table)
    checks=0
    for _ in range(count):
        mode=rng.randrange(7);parts=[]
        for j in range(rng.randrange(1,8)):
            lang='E' if mode==0 or (mode not in (1,2) and rng.random()<.45) else 'J'
            raw=(''.join(rng.choice(tokens) for _ in range(rng.randrange(1,9))) if lang=='J'
                 else ''.join(rng.choice('abcdefghijklmnopqrstuvwxyz') for _ in range(rng.randrange(1,19))))
            parts.append((raw,lang))
        full=''.join(s for s,l in parts)
        at=0;gold=[]
        for s,l in parts:
            if l=='E':gold.append((at,at+len(s)))
            at+=len(s)
        if at!=len(full) or any(full[a:b]!=s for (a,b),(s,l) in zip(gold,[p for p in parts if p[1]=='E'])):
            raise AssertionError('invalid gold')
        if rng.randrange(10)==0:
            a,b=('shi','si') if rng.random()<.5 else ('chi','ti')
            assert as_kana(a,table)==as_kana(b,table)
            checks+=1
    return {'cases':count,'invariant_failures':0,'alias_equivalence_checks':checks}

def h7_bonus(edge,raw,cov):
    st,en=edge[:2];left,right=raw[:st],raw[en:]
    if not left and not right:return 0.
    if not left:return .45*(1. if cov(right)>.99999 else -.5)+.5*(1-cov(raw[en-1]+right))
    if not right:return .45*(1. if cov(left)>.99999 else -.5)+.5*(1-cov(left+raw[st]))
    return score_boundary(edge,raw,cov)

def h7_value(edge,raw,cov):
    return score(edge,RECIPES['char_phonotactic_boundary'])+.9*h7_bonus(edge,raw,cov)

def h7_select(edges,raw,cov,threshold,cap=4):
    out=[e for e in edges if e[4] or h7_value(e,raw,cov)>threshold]
    out.sort(key=lambda e:(h7_value(e,raw,cov)+(.3 if e[4] else 0),e[1]-e[0],-e[0]),reverse=True)
    return out[:cap]

def h7_calibrate(negative,cov,budget=.05):
    allowed=math.floor(len(negative)*budget)
    fixed=sum(any(e[4] for e in edges) for _,edges in negative)
    if fixed>allowed:return None
    vals=sorted((max((h7_value(e,raw,cov) for e in es),default=-1e6)
                 for raw,es in negative if not any(e[4] for e in es)),reverse=True)
    rem=allowed-fixed
    th=vals[rem] if rem<len(vals) else -1e6
    assert sum(bool(h7_select(es,raw,cov,th)) for raw,es in negative)<=allowed
    return th

def metric(case,out):
    gold=[tuple(z) for z in case['gold']]
    predicted=tuple(out[0][:2]) if out else None
    ranges=[tuple(e[:2]) for e in out]
    hits=sum(span in ranges for span in gold)
    true=set(case['switches']);got=set()
    if predicted:
        for i in predicted:
            if 0<i<len(case['raw']) and case['raw'][i-1:i+1].isalpha():got.add(i)
    true={i for i in true if case['raw'][i-1:i+1].isalpha()}
    tp=len(true&got)
    dstart=abs(predicted[0]-gold[0][0]) if predicted and len(gold)==1 else None
    dend=abs(predicted[1]-gold[0][1]) if predicted and len(gold)==1 else None
    return {'rank1_exact':len(gold)==1 and predicted==gold[0],
     'top4_gold':hits,'gold_spans':len(gold),
     'false_case':not gold and predicted is not None,
     'within_one':dstart is not None and dstart<=1 and dend<=1,
     'tp':tp,'fp':len(got-true),'fn':len(true-got),
     'start_distance':dstart,'end_distance':dend}

def run(root,outdir,seeds,structural_count=100000):
    ctx=construct_models(root,seeds[0])
    table=romaji_table(root)
    cases=generate_cases(ctx['oov'],ctx['jtest']+ctx['phon'],table,ALIASES)
    fuzz=structural_fuzz(table,seeds[0],structural_count)
    results={}
    for seed in seeds:
        con=construct_models(root,seed);cov=phonology(load_kana_tokens(root))
        devraw=japanese_negative_streams(con['jdev'],size=100)
        dev=[(raw,decorate(raw,score_edges(raw,con['diff']),cov)) for raw in devraw]
        th5=calibrate([es for _,es in dev],RECIPES['char_phonotactic_boundary'],.05)
        th6=h6_calibrate(dev,cov,.05)
        th7=h7_calibrate(dev,cov,.05)
        if None in (th5,th6,th7):raise ValueError('infeasible calibration')
        @functools.lru_cache(maxsize=30000)
        def edges(raw):return decorate(raw,score_edges(raw,con['diff']),cov)
        counters={m:{g:collections.Counter() for g in GROUPS} for m in ('H5','H6','H7')}
        pairs={m:collections.defaultdict(list) for m in ('H5','H6','H7')}
        errors={m:collections.defaultdict(list) for m in ('H5','H6','H7')}
        for case in cases:
            raw=case['raw'];es=edges(raw)
            preds={'H5':selected(es,RECIPES['char_phonotactic_boundary'],th5,4),
                   'H6':h6_select(es,raw,cov,th6,4),
                   'H7':h7_select(es,raw,cov,th7,4)}
            for method,out in preds.items():
                met=metric(case,out)
                v=counters[method][case['group']]
                v.update(n=1,rank1_exact=int(met['rank1_exact']),top4_gold=met['top4_gold'],
                         gold_spans=met['gold_spans'],false_cases=int(met['false_case']),
                         within_one=int(met['within_one']),tp=met['tp'],fp=met['fp'],fn=met['fn'],
                         selected=int(bool(out)))
                if met['start_distance'] is not None:
                    v.update(distance_n=1,start_distance_total=met['start_distance'],
                             end_distance_total=met['end_distance'])
                if case['pair']:pairs[method][case['pair']].append(met['rank1_exact'])
                if not met['rank1_exact'] and len(case['gold'])==1 and len(errors[method][case['group']])<5:
                    errors[method][case['group']].append({
                      'input':raw,'gold':case['gold'],'rank1':list(out[0][:2]) if out else None})
        data={}
        for method in ('H5','H6','H7'):
            groupdata={}
            for group,c in counters[method].items():
                o=dict(c)
                tp,fp,fn=o.get('tp',0),o.get('fp',0),o.get('fn',0)
                o['boundary_micro_f1']=round(2*tp/(2*tp+fp+fn),4) if 2*tp+fp+fn else None
                o['rank1_exact_pct']=round(100*o.get('rank1_exact',0)/o.get('n',1),2)
                groupdata[group]=o
            pairvals=[x for x in pairs[method].values() if len(x)==2]
            data[method]={'by_group':groupdata,'alias_pairs':{
             'count':len(pairvals),'both_exact':sum(all(x) for x in pairvals),
             'same_correctness':sum(x[0]==x[1] for x in pairvals)},'errors':dict(errors[method])}
        results[str(seed)]={'thresholds':{'H5':th5,'H6':th6,'H7':th7},**data}
    summary={'study':'H7 mixed stress','scope':'Python offline candidate model, NOT C# / IME',
      'cmudict':'1.1.3','per_seed_cases':len(cases),'per_seed_groups':dict(collections.Counter(x['group'] for x in cases)),
      'structural_fuzz':fuzz,'seeds':seeds,'results':results}
    outdir.mkdir(parents=True,exist_ok=True)
    (outdir/'h7-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'per_seed_cases':len(cases),'structural_fuzz':fuzz,
      'seeds':seeds},ensure_ascii=False))

if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--root',type=Path,default=Path('.'))
    p.add_argument('--output-dir',type=Path,default=Path('h7-results'))
    p.add_argument('--seeds',nargs='+',type=int,default=[20261010,20261011,20261012,20261013,20261014])
    p.add_argument('--structural-fuzz',type=int,default=100000)
    a=p.parse_args()
    run(a.root,a.output_dir,a.seeds,a.structural_fuzz)
