#!/usr/bin/env python3
"""H4 offline candidate generator: calibrated false-candidate budget, no IME change.

This script is a separate diagnostic; output is not C# / Windows IME accuracy.
Usage: python research/h4_error_budget_experiment.py --root . --output research/h4-results.json
Requires cmudict and the neighbouring oov_character_model_experiment.py.
"""
import argparse
import functools
import json
import math
import random
from pathlib import Path
import cmudict
from oov_character_model_experiment import Markov, corpus, ENDINGS

PARTICLES = ('ha', 'ga', 'wo', 'ni', 'de', 'to', 'mo', 'no', 'yo')
PREFIXES = ('kyouha', 'korega', 'watashino', 'imanode')
STOP_EXTRA = ('japanese', 'tokyo', 'ime', 'node', 'monitor', 'customer')
VERSION = 'h4-offline-v1'


def shape(w):
    return 7 <= len(w) <= 24 and w.endswith(ENDINGS)


def stream(pre, word, post):
    s = pre + word + post
    return (s, (len(pre), len(pre) + len(word)))


def english_streams(words):
    return [stream(PREFIXES[i % 4], w, PARTICLES[i % len(PARTICLES)]) for i,w in enumerate(words)]


def japanese_negative_streams(words, *, size=0):
    """Generate controls from held-out *word types*; no evaluation words in calibration.

    All negatives are 'intent labels' for artificial unspaced Japanese runs.
    Occasional Latin-looking substrings may be linguistically ambiguous.
    """
    assert words
    samples = []
    # One phonetic/lexical item between known Japanese context.
    for i,w in enumerate(words):
        samples.append(stream(PREFIXES[i % 4], w, PARTICLES[i % len(PARTICLES)])[0])
    # Pair and triple concatenations, independent of arbitrary dataset shuffle.
    for i in range(max(2 * len(words), size)):
        a=words[i % len(words)]
        b=words[(i * 7 + 3) % len(words)]
        c=PARTICLES[i % len(PARTICLES)]
        raw=PREFIXES[(i+1) % 4] + a + c + b + PARTICLES[(i+2) % len(PARTICLES)]
        samples.append(raw)
        if i % 2 == 0:
            raw2=PREFIXES[(i+2)%4]+b+PARTICLES[(i+1)%len(PARTICLES)]+a+c+words[(i*11+1)%len(words)]
            samples.append(raw2)
    # preserve duplicates intentionally only if raw input differs; unique streams
    return list(dict.fromkeys(samples))


def spans(raw):
    """Same span range and suffix/particle cut eligibility as the earlier H1 scanner.

    This does NOT reproduce all C# edge generators and contains no segmentation graph.
    """
    result=[]
    for start in range(len(raw)):
        for end in range(start+7,min(len(raw),start+24)+1):
            w=raw[start:end]
            if not w.isascii() or not w.isalpha():
                continue
            if end<len(raw) and not any(raw.startswith(p,end) for p in PARTICLES):
                continue
            result.append((start,end,w))
    return result


def construct_models(root,seed):
    ja,oov,phon=corpus(root)
    rng=random.Random(seed)
    jshuffle=ja[:]
    rng.shuffle(jshuffle)
    jtrain,jdev,jtest=jshuffle[:55],jshuffle[55:70],jshuffle[70:]
    stop=set(ja+phon+oov+list(STOP_EXTRA))
    english=sorted(set(w.lower() for w in cmudict.words() if w.isascii() and w.isalpha() and 3<=len(w)<=24 and w.lower() not in stop))
    rng.shuffle(english)
    edev,etrain=english[:450],english[450:]
    jm,em=Markov(jtrain),Markov(etrain)
    @functools.lru_cache(maxsize=500000)
    def diff(w):
        return em.likelihood(w)-jm.likelihood(w)
    # Reference H1 threshold from the original isolated-word calibration.
    vals=sorted(set(round(diff(w),5) for w in jdev+edev)|{0.0})
    elig=[]
    for th in vals:
        if sum(diff(w)>th for w in jdev) <= math.floor(len(jdev)*0.05):
            elig.append((sum(diff(w)>th for w in edev)/len(edev), -th, th))
    baseline_th=max(elig)[2]
    return dict(ja=ja,oov=oov,phon=phon,jtrain=jtrain,jdev=jdev,jtest=jtest,edev=edev,
                diff=diff,baseline_threshold=baseline_th,english_train_count=len(etrain))


def score_edges(raw,diff):
    return [(start,end,w,diff(w),shape(w)) for start,end,w in spans(raw)]


def select_edges(edges, *, threshold, cap, mode):
    if mode=='suffix':
        ranked=[e for e in edges if e[4]]
    elif mode=='h1':
        ranked=[e for e in edges if e[4] or e[3]>threshold]
    elif mode=='h4':
        ranked=[e for e in edges if e[4] or e[3]>threshold]
    else:
        raise ValueError(mode)
    # Stronger character evidence before morphology-only, longer spans
    # to discourage 'str' fragments without importing an English lexicon.
    ranked.sort(key=lambda e:(e[3] + (0.30 if e[4] else 0), e[1]-e[0], -e[0]),reverse=True)
    if cap is not None:
        ranked=ranked[:cap]
    return ranked


def metrics(positives, negatives, *, threshold, cap, mode):
    found=0
    outputs=0
    exact_selected=0
    pos_with_spurious=0
    for edges,gold in positives:
        selected=select_edges(edges,threshold=threshold,cap=cap,mode=mode)
        found+= any((e[0],e[1])==gold for e in selected)
        exact_selected+= int(bool(selected) and (selected[0][0],selected[0][1])==gold)
        outputs+=len(selected)
        pos_with_spurious+=int(any((e[0],e[1])!=gold for e in selected))
    neg_cases=0
    neg_edges=0
    for edges in negatives:
        selected=select_edges(edges,threshold=threshold,cap=cap,mode=mode)
        neg_cases+=int(bool(selected))
        neg_edges+=len(selected)
    return dict(positives=len(positives),gold_recovered=found,recall=round(found/len(positives),4),
                rank1_gold=exact_selected,positive_spans=outputs,positive_cases_with_spurious=pos_with_spurious,
                negatives=len(negatives),negatives_with_candidates=neg_cases,
                false_case_rate=round(neg_cases/len(negatives),4),false_spans=neg_edges)


def prepare(raws,diff):
    return [score_edges(raw,diff) for raw in raws]


def run(root,seed,budget=0.05,calibration_limit=180):
    ctx=construct_models(root,seed)
    dev_english=[w for w in ctx['edev'] if 7<=len(w)<=24]
    # Edev is always disjoint from eval OOV. Fixed-size slice avoids dataset cherry-picking.
    dev_english=dev_english[:calibration_limit]
    if len(dev_english)<80: raise ValueError('Too few development English words')
    # Restrict calibration controls to 15 dev Japanese types.
    dev_negative_raw=japanese_negative_streams(ctx['jdev'],size=100)
    test_negative_raw=japanese_negative_streams(ctx['jtest']+ctx['phon'],size=130)
    dev_positive=english_streams(dev_english)
    eval_positive=english_streams(ctx['oov'])
    pd=[(score_edges(raw,ctx['diff']),gold) for raw,gold in dev_positive]
    pe=[(score_edges(raw,ctx['diff']),gold) for raw,gold in eval_positive]
    nd=prepare(dev_negative_raw,ctx['diff'])
    ne=prepare(test_negative_raw,ctx['diff'])

    # FPR-calibrated: only development Japanese *stream cases* influence the limit.
    # We optimize recall under that constraint, then prefer fewer total spurious edges.
    # The test negatives are never accessed before choosing threshold/cap.
    allowed=math.floor(budget * len(nd))
    # Candidate FPR under threshold is monotone. The lowest feasible threshold
    # is enough: lowering it cannot displace a higher-ranked edge at fixed cap.
    unavoidable=sum(any(e[4] for e in edges) for edges in nd)
    if unavoidable>allowed:
        raise ValueError('Morphology-only false positives exceed the calibration budget')
    negative_max_scores=[]
    for edges in nd:
        if any(e[4] for e in edges):
            continue
        negative_max_scores.append(max((e[3] for e in edges),default=-1e9))
    negative_max_scores.sort(reverse=True)
    extra_allow=allowed-unavoidable
    selected_th=negative_max_scores[extra_allow] if extra_allow<len(negative_max_scores) else -1e6
    # Differences are double-precision; equality is safely rejected because
    # the scoring rule selects diff(w) > threshold.
    candidates=[]
    for cap in (1,2,3,4):
        m=metrics(pd,nd,threshold=selected_th,cap=cap,mode='h4')
        if m['negatives_with_candidates']>allowed:
            raise AssertionError('Calibrated FPR unexpectedly exceeded budget')
        candidates.append((m['gold_recovered'], -m['positive_cases_with_spurious'],
                           -m['positive_spans'],-cap,selected_th,cap,m))
    if not candidates:
        raise ValueError('No feasible threshold including high sentinel')
    best=max(candidates)
    selected_th=best[4]
    selected_cap=best[5]
    eval_metrics={
      'suffix':metrics(pe,ne,threshold=ctx['baseline_threshold'],cap=None,mode='suffix'),
      'h1_hybrid':metrics(pe,ne,threshold=ctx['baseline_threshold'],cap=None,mode='h1'),
      'h4_calibrated':metrics(pe,ne,threshold=selected_th,cap=selected_cap,mode='h4'),
      'h4_threshold_uncapped':metrics(pe,ne,threshold=selected_th,cap=None,mode='h4'),
    }
    dev_selected=best[-1]
    diff=ctx['diff']
    errors={}
    for name,mode,threshold,cap in [('h1_hybrid','h1',ctx['baseline_threshold'],None),('h4_calibrated','h4',selected_th,selected_cap)]:
        errors[name]={
         'missed_oov':[word for (raw,gold),word,(edges,_) in zip(eval_positive,ctx['oov'],pe)
                       if not any((e[0],e[1])==gold for e in select_edges(edges,threshold=threshold,cap=cap,mode=mode))],
         'false_negative_examples':[raw for raw,edges in zip(test_negative_raw,ne)
                             if select_edges(edges,threshold=threshold,cap=cap,mode=mode)][:10]
        }
    # Upper bound: independent observations would be invalid because controls reuse types.
    return {
      'experiment':VERSION,'seed':seed,'cmudict_version':getattr(cmudict,'__version__','unknown'),
      'budget_dev_false_case_rate':budget,'baseline_threshold':round(ctx['baseline_threshold'],6),
      'chosen_threshold':round(selected_th,6),'chosen_cap':selected_cap,
      'dev_english_types':len(dev_english),'dev_japanese_types':len(ctx['jdev']),
      'eval_english_types':len(ctx['oov']),'eval_japanese_types':len(ctx['jtest']+ctx['phon']),
      'dev_positive_streams':len(pd),'dev_negative_streams':len(nd),
      'test_positive_streams':len(pe),'test_negative_streams':len(ne),
      'english_train_count':ctx['english_train_count'],
      'dev_calibrated':dev_selected,'test':eval_metrics,'examples':errors,
      'calibrated_cap_options_evaluated':len(candidates),
      'limitations':['Synthetic streams, not independently annotated natural input',
        'Japanese stream negatives reuse a small number of unique word types',
        'Not C# StreamHybridDecoder or IME integration',
        'Threshold and cap chosen only from development streams but preprocessing choices are researcher-designed']
    }


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--root',type=Path,default=Path('.'))
    p.add_argument('--output',type=Path)
    p.add_argument('--seeds',type=int,nargs='+',default=[20261010,20261011,20261012,20261013,20261014])
    p.add_argument('--budget',type=float,default=0.05)
    p.add_argument('--calibration-limit',type=int,default=180)
    args=p.parse_args()
    if not 0 <= args.budget <= 1: p.error('--budget must be within [0,1]')
    data={'study':'H4 calibrated English candidate budget',
      'records':[run(args.root,seed,args.budget,args.calibration_limit) for seed in args.seeds]}
    formatted=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(formatted,encoding='utf-8')
    print(json.dumps({'seeds':args.seeds,'budget':args.budget,
      'summary':[{ 'seed':r['seed'], 'threshold':r['chosen_threshold'],'cap':r['chosen_cap'],
        'h1':r['test']['h1_hybrid'], 'h4':r['test']['h4_calibrated']}
       for r in data['records']]},ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
