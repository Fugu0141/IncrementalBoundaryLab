#!/usr/bin/env python3
"""H7 / mixed Japanese-romaji and English stress audit of H5/H6 candidate rankers.

Run: python h7_mixed_stream_stress.py --root REPO --output-dir OUTPUT --seeds 20261010 20261013
Tests only an offline English-span candidate proxy, NOT the actual StreamHybrid decoder/IME.
"""
from __future__ import annotations
import argparse, collections, hashlib, itertools, json, random, sys, time
from pathlib import Path
from functools import lru_cache

# Imports existing research code from --root/research in main().
GROUPS = ('start','middle','end','english_only','two_islands','adjacent_english',
          'short_literal','romanization_pairs','symbols','case_variants',
          'no_english','nonsense_literal','inside_mora')


def generate_cases(oov, japanese, table, aliases):
    """Deterministic Cartesian strata, without using selected scores to select examples.

    Japanese segments are typed in ASCII; gold spans are raw input offsets. An English
    island can appear at any offset; multi-island cases are retained even though
    H5/H6's rank1 can output only one span. Do not call those parseable by a full IME.
    """
    from h6_boundary_alias_experiment import as_kana
    expected=[]
    def add(group, chunks, pair=None, tags=()):
        raw=''.join(text for text,lang in chunks)
        if len(raw)>108: return
        spans=[];switches=[];at=0;prev=None
        for text,lang in chunks:
            if text and prev is not None and prev!=lang and {prev,lang}=={'J','E'}:switches.append(at)
            if lang=='E' and text:spans.append((at,at+len(text)))
            if text:prev=lang
            at+=len(text)
        expected.append(dict(id=len(expected),group=group,raw=raw,
                             gold=[list(x) for x in spans],switches=switches,pair=pair,tags=list(tags)))
    primary=list(oov)
    pre=['', 'oreha','watashino','kyouha','shigotode','sigotode','chikakude','tikakude',
         'shashinde','syasinde','ashitano','gakuseiha','sorekara']
    post=['','ha','ga','no','ni','de','to','wo','nishimasu','nisimasu','nitsukau','nitukau','toiu']
    # First / last / central, with all word types covered in each region.
    for i,w in enumerate(primary):
        for j,suffix in enumerate(post[1:]):
            add('start',[(w,'E'),(suffix,'J')],tags=(str(j),))
        for j,prefix in enumerate(pre[1:]):
            add('end',[(prefix,'J'),(w,'E')],tags=(str(j),))
        for j in range(14):
            prefix=pre[1+(i+j)%12]; suffix=post[1+(i*3+j)%12]
            add('middle',[(prefix,'J'),(w,'E'),(suffix,'J')],tags=(str(j),))
        add('english_only',[(w,'E')])
    # Two English islands, two positions: our proxy has no joint-path resolver.
    for i,w in enumerate(primary):
        other=primary[(i*7+11)%len(primary)]
        for j in range(6):
            prefix=pre[1+(i+j)%12]; suffix=post[1+(i+j*2)%12]
            between=('ga','no','ni','de','ha','to')[j]
            add('two_islands',[(prefix,'J'),(w,'E'),(between,'J'),(other,'E'),(suffix,'J')])
            add('adjacent_english',[(prefix,'J'),(w,'E'),(other,'E'),(suffix,'J')])
    # The same Japanese meaning typed in aliases; boundary coordinates differ.
    for i,w in enumerate(primary):
        for j,(p0,p1,q0,q1) in enumerate(aliases):
            assert as_kana(p0,table)==as_kana(p1,table) is not None
            assert as_kana(q0,table)==as_kana(q1,table) is not None
            key=f'{i}:{j}'
            add('romanization_pairs',[(p0,'J'),(w,'E'),(q0,'J')],pair=key,tags=('style0',))
            add('romanization_pairs',[(p1,'J'),(w,'E'),(q1,'J')],pair=key,tags=('style1',))
    # Intent-annotated abbreviations are out of candidate length (7-24), intentionally adversarial.
    short=['ime','api','ui','ai','os','ram','cpu','gpu','js','css','html','git','node','tokyo','go','rust']
    for w in short:
        for j in range(18):
            mode=j%3
            p=pre[1+(j+i)%12];q=post[1+(j*3+i)%12]
            chunks=[(w,'E'),(q,'J')] if mode==0 else ([(p,'J'),(w,'E')] if mode==1 else [(p,'J'),(w,'E'),(q,'J')])
            add('short_literal',chunks,tags=(w,))
    for i,w in enumerate(primary):
        for transform in (str.upper,str.capitalize):
            value=transform(w)
            for j in range(2):
                add('case_variants',[(pre[1+(i+j)%12],'J'),(value,'E'),(post[1+(i+j)%12],'J')],tags=(value,))
    for i,w in enumerate(primary):
        for symbol in ('.','-','_','/','@','#'):
            # Symbols are structural separators, not part of original English Gold span.
            add('symbols',[(pre[1+i%12],'J'),(w,'E'),(symbol,'S'),('no','J')],tags=(symbol,))
    # No-english Japanese controls in diverse synthesized contexts.
    for i,w in enumerate(japanese):
        for j in range(28):
            p=pre[1+(i+j)%12];q=post[1+(j*3+i)%12]
            chunks=[(p,'J'),(w,'J'),(q,'J')]
            if j%2==1:chunks.extend([(japanese[(i*5+j)%len(japanese)],'J'),('ni','J')])
            add('no_english',chunks)
    # Unusual literal intentions, not vocabulary-known English; ambiguity expected.
    nonsense=('qzxvbnmk','zxqvqqtv','abcdefghij','ababababab','rtrtrtrtrt','strstrstrst',
              'xxyzzzyyx','mmnnvvbbcc','ueueueueu','ababzababz','xyzzxyzzxy','qwertyuiop')
    for i,w in enumerate(nonsense):
        for j in range(24):
            p=pre[1+(i+j)%12]; q=post[1+(j+i*2)%12]
            chunks=[(w,'E'),(q,'J')] if j%3==0 else ([(p,'J'),(w,'E')] if j%3==1 else [(p,'J'),(w,'E'),(q,'J')])
            add('nonsense_literal',chunks,tags=(w,))
    # Intentionally cut inside a Japanese mora, unrealistic but tests raw boundary handling.
    for i,w in enumerate(primary):
        for j in range(4):
            broken=('sh','ch','ts','sy')[j]
            add('inside_mora',[(pre[1+i%12]+broken,'J'),(w,'E'),('i'+post[1+(i+j)%12],'J')])
    # Number and coverage of structural cases is deterministic.
    assert all(c['group'] in GROUPS for c in expected)
    assert len(set(c['id'] for c in expected))==len(expected)
    return expected


def structural_fuzz(cases,table,seed,n=30000):
    """Gold coordinate invariants with randomized raw strings and language orders.

    This is a **generator and oracle property check**, not model inference.
    """
    rng=random.Random(seed)
    from h6_boundary_alias_experiment import as_kana
    tokens=list(table)
    n_alias=0
    for _ in range(n):
        mode=rng.randrange(7)
        parts=[]
        for j in range(rng.randrange(1,8)):
            lang='E' if mode==0 or (mode not in (1,2) and rng.random()<.45) else 'J'
            raw=''.join(rng.choice(tokens) for _ in range(rng.randrange(1,9))) if lang=='J' else ''.join(rng.choice('abcdefghijklmnopqrstuvwxyz') for _ in range(rng.randrange(1,19)))
            parts.append((raw,lang))
        full=''.join(t for t,_ in parts)
        cursor=0; gold=[]
        for text,lang in parts:
            if lang=='E':gold.append((cursor,cursor+len(text)))
            cursor+=len(text)
        if cursor!=len(full) or any(full[a:b]!=text for (a,b), (text,lang) in zip(gold, [x for x in parts if x[1]=='E'])):
            raise AssertionError('gold raw-offset mismatch')
        if rng.randrange(10)==0:
            # Alternative romanizations must be semantically equivalent *only* in J segments.
            a,b=('shi','si') if rng.random()<.5 else ('chi','ti')
            assert as_kana(a,table)==as_kana(b,table)
            n_alias+=1
    return {'cases':n,'invariant_failures':0,'alias_equivalence_checks':n_alias}


def metric_for_case(c,selected,rank4):
    gold=[tuple(x) for x in c['gold']]
    gold_set=set(gold)
    pred=tuple(selected[0][:2]) if selected else None
    in4=[tuple(e[:2]) for e in rank4]
    hit=sum(g in in4 for g in gold)
    # Multiple targets cannot be represented by the rank-1 single-island architecture.
    boundaries=set(c['switches'])
    pb=set()
    if pred:
        if 0<pred[0]<len(c['raw']):pb.add(pred[0])
        if 0<pred[1]<len(c['raw']):pb.add(pred[1])
    tp=len(boundaries&pb);fp=len(pb-boundaries);fn=len(boundaries-pb)
    # Ignore punctuation boundary as language switch only where BOTH adjacent chars
    # are letter characters. Keep start/end offsets for English-span exactness.
    if any(not c['raw'][at-1:at+1].isalpha() for at in set(boundaries)|pb):
        keep=lambda points:{at for at in points if c['raw'][at-1:at+1].isalpha()}
        boundaries,pb=keep(boundaries),keep(pb)
        tp=len(boundaries&pb);fp=len(pb-boundaries);fn=len(boundaries-pb)
    return {'gold_n':len(gold),'top4_hits':hit,'all_gold_in_top4':hit==len(gold),
        'rank1_exact':(len(gold)==1 and pred==gold[0]),
        'rank1_any_gold':pred in gold if pred else False,
        'false_english':not gold and pred is not None,
        'start_diff':abs(pred[0]-gold[0][0]) if pred is not None and len(gold)==1 else None,
        'end_diff':abs(pred[1]-gold[0][1]) if pred is not None and len(gold)==1 else None,
        'both_within_one':(pred is not None and len(gold)==1 and
                          abs(pred[0]-gold[0][0])<=1 and abs(pred[1]-gold[0][1])<=1),
        'tp':tp,'fp':fp,'fn':fn,'pred':pred}



def h7_bonus(edge,raw,cov):
    """Exploratory symmetric cut evidence: missing Japanese side is NOT penalized.

    Proposed after H6 failures on start/end were observed; these stress results
    are exploratory, not an independent holdout proving H7 generalization.
    """
    st,en=edge[:2]
    left,right=raw[:st],raw[en:]
    if not left and not right:return 0.0
    if not left:
        return .45*(1.0 if cov(right)>.99999 else -.5)+.5*(1-cov(raw[en-1]+right))
    if not right:
        return .45*(1.0 if cov(left)>.99999 else -.5)+.5*(1-cov(left+raw[st]))
    from h6_boundary_alias_experiment import score_boundary
    return score_boundary(edge,raw,cov)


def h7_value(edge,raw,cov):
    from h5_phonotactic_context_experiment import score, RECIPES
    return score(edge,RECIPES['char_phonotactic_boundary'])+.9*h7_bonus(edge,raw,cov)


def h7_select(edges,raw,cov,threshold,cap=4):
    out=[e for e in edges if e[4] or h7_value(e,raw,cov)>threshold]
    out.sort(key=lambda e:(h7_value(e,raw,cov)+(.3 if e[4] else 0),e[1]-e[0],-e[0]),reverse=True)
    return out[:cap]


def h7_calibrate(negative,cov,budget=.05):
    import math
    allowed=math.floor(len(negative)*budget)
    fixed=sum(any(e[4] for e in edges) for _,edges in negative)
    if fixed>allowed: return None
    vals=sorted((max((h7_value(e,raw,cov) for e in edges),default=-1e6)
                  for raw,edges in negative if not any(e[4] for e in edges)),reverse=True)
    left=allowed-fixed
    th=vals[left] if left<len(vals) else -1e6
    assert sum(bool(h7_select(edges,raw,cov,th)) for raw,edges in negative)<=allowed
    return th

def run(root,outdir,seeds,limit=None,structural_count=30000):
    sys.path.insert(0,str(root/'research'))
    from h6_boundary_alias_experiment import ALIASES,romaji_table,score_boundary,h6_score,h6_select,h6_calibrate
    from h5_phonotactic_context_experiment import (construct_models,phonology,load_kana_tokens,RECIPES,
                                                   decorate,score_edges,selected,calibrate)
    import cmudict
    table=romaji_table(root)
    ctx=construct_models(root,seeds[0])
    cases=generate_cases(ctx['oov'],ctx['jtest']+ctx['phon'],table,ALIASES)
    if limit: cases=cases[:limit]
    fuzz=structural_fuzz(cases,table,seeds[0],structural_count)
    bygroup=collections.Counter(c['group'] for c in cases)
    results={}
    t0=time.monotonic()
    for seed in seeds:
        con=construct_models(root,seed)
        cov=phonology(load_kana_tokens(root))
        rawdev=__import__('h4_error_budget_experiment').japanese_negative_streams(con['jdev'],size=100)
        dev=[(raw,decorate(raw,score_edges(raw,con['diff']),cov)) for raw in rawdev]
        h5th=calibrate([d for _,d in dev],RECIPES['char_phonotactic_boundary'],.05)
        h6th=h6_calibrate(dev,cov,.05)
        h7th=h7_calibrate(dev,cov,.05)
        if h5th is None or h6th is None or h7th is None:raise ValueError('Calibration infeasible')
        agg={kind:{group:collections.Counter() for group in GROUPS} for kind in ('H5','H6','H7')}
        samples={kind:collections.defaultdict(list) for kind in ('H5','H6','H7')}
        # Aliases compare per pair without treating same text as independent word types.
        alias={kind:collections.defaultdict(list) for kind in ('H5','H6','H7')}
        @lru_cache(maxsize=30000)
        def edges(raw):return decorate(raw,score_edges(raw,con['diff']),cov)
        for idx,c in enumerate(cases):
            raw=c['raw']; es=edges(raw)
            out5=selected(es,RECIPES['char_phonotactic_boundary'],h5th,4)
            out6=h6_select(es,raw,cov,h6th,4)
            out7=h7_select(es,raw,cov,h7th,4)
            for kind,out in (('H5',out5),('H6',out6),('H7',out7)):
                m=metric_for_case(c,out,out)
                counter=agg[kind][c['group']];counter.update({'n':1,'gold_spans':m['gold_n'],
                         'top4_gold':m['top4_hits'],'all_gold_in_top4':int(m['all_gold_in_top4']),
                         'rank1_exact':int(m['rank1_exact']),'rank1_any_gold':int(m['rank1_any_gold']),
                         'false_cases':int(m['false_english']), 'within_one':int(m['both_within_one']),
                         'tp':m['tp'],'fp':m['fp'],'fn':m['fn'],
                         'selected':int(bool(out))})
                if m['start_diff'] is not None:
                    counter['start_error_sum']+=m['start_diff'];counter['end_error_sum']+=m['end_diff']
                    counter['distance_n']+=1
                    counter['error_gt_one']+=int(m['start_diff']>1 or m['end_diff']>1)
                if c['pair'] is not None:alias[kind][c['pair']].append(bool(m['rank1_exact']))
                if not m['rank1_exact'] and len(c['gold'])==1 and len(samples[kind][c['group']])<5:
                    samples[kind][c['group']].append({'raw':raw,'gold':c['gold'],
                          'rank1':list(m['pred']) if m['pred'] else None,
                          'gold_in_top4':bool(m['top4_hits']),'seed':seed})
                if m['false_english'] and len(samples[kind][c['group']])<5:
                    samples[kind][c['group']].append({'raw':raw,'gold':[],'rank1':list(m['pred']) if m['pred'] else None,'seed':seed})
            if idx%1500==0:
                print(f'seed={seed}: {idx}/{len(cases)} elapsed={time.monotonic()-t0:.1f}s',flush=True)
        result={}
        for kind in ('H5','H6','H7'):
            data={}
            for group,counts in agg[kind].items():
                c=dict(counts)
                tp,fp,fn=c.get('tp',0),c.get('fp',0),c.get('fn',0)
                c['boundary_micro_f1']=round(2*tp/(2*tp+fp+fn),4) if 2*tp+fp+fn else None
                c['rank1_exact_pct']=round(100*c.get('rank1_exact',0)/c.get('n',1),2)
                c['top4_gold_pct']=round(100*c.get('top4_gold',0)/c.get('gold_spans',1),2) if c.get('gold_spans',0) else None
                c['japanese_false_pct']=round(100*c.get('false_cases',0)/c.get('n',1),2) if group=='no_english' else None
                c['start_mae']=round(c.get('start_error_sum',0)/c.get('distance_n',1),2) if c.get('distance_n',0) else None
                c['end_mae']=round(c.get('end_error_sum',0)/c.get('distance_n',1),2) if c.get('distance_n',0) else None
                data[group]=c
            pair_results=[v for v in alias[kind].values() if len(v)==2]
            result[kind]={'by_group':data,'alias_pairs':{'count':len(pair_results),
                'both_exact':sum(all(v) for v in pair_results),
                'same_correctness':sum(v[0]==v[1] for v in pair_results)},
                'failures':dict(samples[kind])}
        results[str(seed)]={'thresholds':{'H5':round(h5th,6),'H6':round(h6th,6),'H7':round(h7th,6)},**result}
        print(f'SEED DONE {seed} time={time.monotonic()-t0:.1f}s',flush=True)
    final={'study':'H7 mixed-language position and alias stress audit','date':'2026-10-10',
      'scope':'Python offline H5/H6 English-island rankers only, not C# StreamHybrid or IME',
      'cmudict':getattr(cmudict,'__version__','unknown'),
      'words_held_out_english':54,'japanese_words_test':34,
      'per_seed_cases':len(cases),'per_seed_groups':dict(bygroup),
      'structural_fuzz':fuzz,'seeds':seeds,'results':results,
      'caveats':['All samples synthesized; repeated word types and templates, not independent real-world user inputs',
        'A single selected English island cannot model two true islands; performance for multi-island is diagnostic, not full IME accuracy',
        'No punctuation handling in H5/H6 Latin span scanner; structural symbols deliberately stress scope',
        'Short and nonsense literal intent cannot be resolved from characters alone; annotations are test assumptions',
        'Only Japanese/English language-switch boundaries measured; Japanese internal word boundaries not evaluated',
        'Structural fuzz checks generator/oracle arithmetic; does not test model predictions',
        'No actual .NET, TSF, Windows app, or Mozc input was executed']}
    outdir.mkdir(parents=True,exist_ok=True)
    (outdir/'h7-results.json').write_text(json.dumps(final,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    # Publish compact summary and selected failures instead of gigantic raw dumps.
    compact={'study':final['study'],'scope':final['scope'],'cmudict':final['cmudict'],
       'per_seed_cases':len(cases),'per_seed_groups':dict(bygroup),'structural_fuzz':fuzz,
       'seeds':seeds,'results':{s:{'thresholds':x['thresholds'],
                  'H5':{'by_group':x['H5']['by_group'],'alias_pairs':x['H5']['alias_pairs']},
                  'H6':{'by_group':x['H6']['by_group'],'alias_pairs':x['H6']['alias_pairs']},
                  'H7':{'by_group':x['H7']['by_group'],'alias_pairs':x['H7']['alias_pairs']}}
                    for s,x in results.items()},'caveats':final['caveats']}
    (outdir/'h7-summary.json').write_text(json.dumps(compact,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    (outdir/'h7-failure-samples.json').write_text(json.dumps({s:{kind:x[kind]['failures']
                for kind in ('H5','H6','H7')} for s,x in results.items()},ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps({'per_seed_cases':len(cases),'groups':dict(bygroup),'structural_fuzz':fuzz,
      'quick':{s:{'middle_h5':v['H5']['by_group']['middle']['rank1_exact_pct'],
                  'middle_h6':v['H6']['by_group']['middle']['rank1_exact_pct'],
                  'middle_h7':v['H7']['by_group']['middle']['rank1_exact_pct'],
                  'start_h7':v['H7']['by_group']['start']['rank1_exact_pct'],
                  'end_h7':v['H7']['by_group']['end']['rank1_exact_pct'],
                  'japanese_false_h5':v['H5']['by_group']['no_english'].get('japanese_false_pct'),
                  'japanese_false_h6':v['H6']['by_group']['no_english'].get('japanese_false_pct'),
                  'japanese_false_h7':v['H7']['by_group']['no_english'].get('japanese_false_pct')}
        for s,v in results.items()}},ensure_ascii=False,indent=2))

if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--root',type=Path,required=True)
    p.add_argument('--output-dir',type=Path,required=True)
    p.add_argument('--seeds',type=int,nargs='+',default=[20261010,20261013])
    p.add_argument('--limit',type=int,default=None)
    p.add_argument('--structural-fuzz',type=int,default=30000)
    a=p.parse_args()
    run(a.root,a.output_dir,a.seeds,a.limit,a.structural_fuzz)
