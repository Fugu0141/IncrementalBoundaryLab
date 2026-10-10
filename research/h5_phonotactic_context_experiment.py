#!/usr/bin/env python3
"""H5: contrast English char evidence, Japanese romaji readability, and local boundaries.

Diagnostic candidate generator, NOT C# decoder/IME behavior.
Development-only calibration; per-word OOV and Japanese groups are held out.
Requires cmudict and research/{h4_error_budget_experiment,oov_character_model_experiment}.py.
"""
from __future__ import annotations
import argparse
import functools
import json
import math
import re
from pathlib import Path
from h4_error_budget_experiment import (construct_models, english_streams,
    japanese_negative_streams, score_edges, PREFIXES, PARTICLES)

RECIPES = {
    'char_only': (0., 0.),
    'char_phonotactic': (1., 0.),
    'char_boundary': (0., 0.8),
    'char_phonotactic_boundary': (1., 0.8),
}
CAP = 4
BUDGET = 0.05


def load_kana_tokens(root: Path):
    source = (root/'src/BoundaryLab.Core/RomajiConverter.cs').read_text(encoding='utf8')
    pairs = re.findall(r'\["([a-z]+)"\]="([^"]+)"', source)
    # Source sometimes has whitespace around =
    if not pairs:
        pairs = re.findall(r'\["([a-z]+)"\]\s*=\s*"([^"]+)"', source)
    if len(pairs)<50:
        raise ValueError(f'Only {len(pairs)} kana tokens; source parsing failed')
    return frozenset(k for k,_ in pairs)


def phonology(tokens):
    """Maximum fraction of chars explained as romaji. Partial readings penalized.

    DP allows skipping any char, because unknown words often mix unreadable and
    readable syllables. Rules for gemination and n mirror the offline tokenizer
    at a coarse level; this is not a 1:1 port of RomajiConverter.
    """
    @functools.lru_cache(maxsize=500000)
    def coverage(word):
        if not word: return 1.0
        n=len(word)
        dp=[0]*(n+1)
        for i in range(n):
            dp[i+1]=max(dp[i+1],dp[i])
            for length in (1,2,3):
                if i+length<=n and word[i:i+length] in tokens:
                    dp[i+length]=max(dp[i+length],dp[i]+length)
            if i+1<n and word[i]==word[i+1] and word[i] not in 'aiueon':
                dp[i+1]=max(dp[i+1],dp[i]+1)
            if word[i]=='n' and (i==n-1 or word[i+1] not in 'aiueoy'):
                dp[i+1]=max(dp[i+1],dp[i]+1)
        return dp[n]/n
    return coverage


def context_score(raw,start,end,cov):
    # No gold boundaries or literal PREFIXES/PARTICLES consulted here.
    # Japanese outside the candidate should itself form readable romaji.
    # Empty prefix is intentionally neutral, not stronger than genuine context.
    left=raw[:start]
    right=raw[end:]
    lp=cov(left) if left else 0.5
    rp=cov(right) if right else 0.5
    return (lp+rp)/2 - .5


def decorate(raw, edges, cov):
    # H4 edge: start,end,word,char_diff,is_suffix_morphology
    # H5 edge adds two independent diagnostics; do not use test labels.
    return [(st,en,word,diff,shape,1-cov(word),context_score(raw,st,en,cov))
            for st,en,word,diff,shape in edges]


def score(e,weights):
    return e[3]+weights[0]*e[5]+weights[1]*e[6]


def selected(edges,weights,threshold,cap=CAP):
    # morphology candidates are always kept as H4 did; possible budget floor.
    out=[e for e in edges if e[4] or score(e,weights)>threshold]
    out.sort(key=lambda e:(score(e,weights)+(.30 if e[4] else 0), e[1]-e[0],-e[0]),reverse=True)
    return out[:cap] if cap is not None else out


def measure(positives, negatives, weights, threshold, cap=CAP):
    count=0;rank1=0;proposed=0;spurious=0;bad=0;bad_spans=0
    for edges,gold in positives:
        out=selected(edges,weights,threshold,cap)
        count+=any((e[0],e[1])==gold for e in out)
        rank1+=bool(out) and (out[0][0],out[0][1])==gold
        proposed+=len(out)
        spurious+=any((e[0],e[1])!=gold for e in out)
    for edges in negatives:
        out=selected(edges,weights,threshold,cap)
        bad+=bool(out)
        bad_spans+=len(out)
    return {'recalled':int(count),'recall':round(count/len(positives),4),
            'rank1':int(rank1),'positive_spans':proposed,
            'positive_with_spurious':int(spurious),'false_cases':bad,
            'false_case_rate':round(bad/len(negatives),4),
            'false_spans':bad_spans,'positive_n':len(positives),'negative_n':len(negatives)}


def calibrate(negatives,weights,budget=BUDGET):
    allowed=math.floor(budget*len(negatives))
    # Unavoidable morphology cannot be removed by a score cutoff.
    unavoidable=sum(any(e[4] for e in es) for es in negatives)
    if unavoidable>allowed:
        return None
    nonmorph_scores=sorted((max((score(e,weights) for e in es),default=-1e6)
                           for es in negatives if not any(e[4] for e in es)),reverse=True)
    rem=allowed-unavoidable
    threshold=nonmorph_scores[rem] if rem<len(nonmorph_scores) else -1e6
    assert sum(bool(selected(es,weights,threshold)) for es in negatives)<=allowed
    return threshold



# Evaluation-only domain-shift stress. Neither the threshold nor recipe weights
# are fitted on these contexts. Different Japanese prefix/suffix vocabulary;
# do NOT treat as naturally sampled conversation.
SHIFT_PREFIXES=('ashitano','sorekara','gakuseiha','orenimo')
SHIFT_SUFFIXES=('gaaru','nidesu','dekita','nohon','toiu','mosugu','wosuru')


def shifted_english_streams(words):
    return [(SHIFT_PREFIXES[i%len(SHIFT_PREFIXES)] + w + SHIFT_SUFFIXES[i%len(SHIFT_SUFFIXES)],
             (len(SHIFT_PREFIXES[i%len(SHIFT_PREFIXES)]),
              len(SHIFT_PREFIXES[i%len(SHIFT_PREFIXES)])+len(w)))
            for i,w in enumerate(words)]


def shifted_japanese_streams(raws):
    repl=dict(zip(PREFIXES,SHIFT_PREFIXES))
    result=[]
    for raw in raws:
        prefix=next((p for p in PREFIXES if raw.startswith(p)),None)
        result.append(repl[prefix]+raw[len(prefix):] if prefix else raw)
    return result


def run(root,seed=20261010,budget=BUDGET):
    ctx=construct_models(root,seed)
    cov=phonology(load_kana_tokens(root))
    dev_english=[w for w in ctx['edev'] if 7<=len(w)<=24][:180]
    positive_dev_raw=english_streams(dev_english)
    negative_dev_raw=japanese_negative_streams(ctx['jdev'],size=100)
    positive_test_raw=english_streams(ctx['oov'])
    negative_test_raw=japanese_negative_streams(ctx['jtest']+ctx['phon'],size=130)
    def pos(items):
        return [(decorate(s,score_edges(s,ctx['diff']),cov),gold) for s,gold in items]
    def neg(items):
        return [decorate(s,score_edges(s,ctx['diff']),cov) for s in items]
    pd,nd=pos(positive_dev_raw),neg(negative_dev_raw)
    pt,nt=pos(positive_test_raw),neg(negative_test_raw)
    # Frozen, uncalibrated shift using different context strings.
    shifted_pt=pos(shifted_english_streams(ctx['oov']))
    shifted_nt=neg(shifted_japanese_streams(negative_test_raw))
    # Compare each recipe at the same empirical DEVELOPMENT FPR bound.
    # No held-out labels used during threshold choice.
    rows={}
    for name,w in RECIPES.items():
        th=calibrate(nd,w,budget)
        if th is None:
            rows[name]={'status':'unfeasible morphology floor'}
            continue
        rows[name]={'threshold':round(th,6),'dev':measure(pd,nd,w,th),
                    'test':measure(pt,nt,w,th),
                    'test_uncapped':measure(pt,nt,w,th,None),
                    'context_shift':measure(shifted_pt,shifted_nt,w,th)}
    # Select H5 recipe on development recall, break ties by fewer spurious candidates;
    # baseline included only for transparent comparison, not eligible for selection.
    modes=('char_phonotactic','char_boundary','char_phonotactic_boundary')
    eligible=[(rows[m]['dev']['recalled'],-rows[m]['dev']['positive_with_spurious'],
               -rows[m]['dev']['positive_spans'],m) for m in modes if rows[m].get('dev')]
    chosen=max(eligible)[-1] if eligible else None
    # Pin H4's actual selection as an external independent control, same streams.
    from h4_error_budget_experiment import run as h4_run
    h4=h4_run(root,seed,budget)
    return {'seed':seed,'budget':budget,'cmudict':'1.1.3 expected',
        'english_heldout_types':len(ctx['oov']),'japanese_heldout_types':len(ctx['jtest']+ctx['phon']),
        'development_english_types':len(dev_english),'development_japanese_types':len(ctx['jdev']),
        'dev_negative_cases':len(nd),'test_negative_cases':len(nt),
        'baseline_h4':h4['test']['h4_calibrated'],
        'h4_threshold':h4['chosen_threshold'],
        'recipes':rows,'selected_on_dev':chosen,
        'selected_test':rows[chosen]['test'] if chosen else None,
        'selected_context_shift':rows[chosen]['context_shift'] if chosen else None,
        'caveats':['H5 candidate-only synthetic streams, not C# or actual IME','Word types re-used across negative glued streams; holdout samples not independent','H4 has cap selected on dev; H5 cap fixed at 4','H5 feature weights were specified before running test but recipes chosen on dev','False-case-rate is not a bound on unknown input distribution']}


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--root',type=Path,default=Path('.'))
    p.add_argument('--budget',type=float,default=BUDGET)
    p.add_argument('--seeds',type=int,nargs='+',default=[20261010,20261011,20261012,20261013,20261014])
    p.add_argument('--output',type=Path)
    a=p.parse_args()
    if not 0<=a.budget<=1: p.error('budget must be between 0 and 1')
    obj={'study':'H5 phonotactic and boundary evidence ablation', 'records':[run(a.root,seed,a.budget) for seed in a.seeds]}
    out=json.dumps(obj,ensure_ascii=False,indent=2)+'\n'
    if a.output:
        a.output.parent.mkdir(parents=True,exist_ok=True)
        a.output.write_text(out,encoding='utf8')
    print(json.dumps({'by_seed':[{ 'seed':x['seed'],'h4':x['baseline_h4'],
         'selected':x['selected_on_dev'],'selected_test':x['selected_test'],
         'selected_context_shift':x['selected_context_shift'],
         'ablations':{name:d.get('test') for name,d in x['recipes'].items()}} for x in obj['records']]},ensure_ascii=False,indent=2))

if __name__=='__main__': main()
