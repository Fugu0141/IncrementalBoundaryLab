#!/usr/bin/env python3
"""H6: boundary placement and romaji-alias metamorphic tests of H5 candidates.

The English span is *intended*, not inferred from text. This is an offline
candidate-ranking experiment, not Windows IME/Mozc. Requires research H4/H5.
"""
import argparse
import json
import math
import statistics
from pathlib import Path
from functools import lru_cache

from h5_phonotactic_context_experiment import (construct_models, phonology,
    load_kana_tokens, decorate, score_edges, RECIPES, calibrate, selected,
    english_streams, japanese_negative_streams, context_score, score)
from h4_error_budget_experiment import PARTICLES

ALIASES=[
    ('shigotode','sigotode','nishimasu','nisimasu'),
    ('chikakude','tikakude','nitsukau','nitukau'),
    ('futaride','hutaride','nishashino','nisyasino'),
    ('shashinde','syasinde','nichikaku','nitikaku'),
    ('shouhinno','syouhinno','nichousa','nityousa'),
    ('chousano','tyousano','nifutari','nihutari'),
]


def romaji_table(root):
    import re
    source=(root/'src/BoundaryLab.Core/RomajiConverter.cs').read_text(encoding='utf-8')
    d=dict(re.findall(r'\["([a-z]+)"\]\s*=\s*"([^"]+)"',source))
    if not all(x in d for x in ('shi','si','chi','ti','tsu','tu','fu','hu','sha','sya')):
        raise ValueError('Missing required dual romanization tokens')
    return d


def as_kana(text,mapping):
    """Port TryConsume precedence for checking metamorphic gold contexts."""
    output=[]
    i=0
    while i<len(text):
        if (i+1<len(text) and text[i]==text[i+1] and text[i] not in 'aiueon'):
            output.append('っ');i+=1;continue
        if text[i]=='n':
            if text[i:i+2]=='nn':
                output.append('ん');i+=2;continue
            if i==len(text)-1 or text[i+1] not in 'aiueoy':
                output.append('ん');i+=1;continue
        for n in (3,2,1):
            if text[i:i+n] in mapping:
                output.append(mapping[text[i:i+n]]);i+=n;break
        else:
            return None
    return ''.join(output)


def prepare_alias_corpus(words,negative_words,table):
    positive=[]
    negative=[]
    for i,w in enumerate(words):
        pre0,pre1,post0,post1=ALIASES[i%len(ALIASES)]
        assert as_kana(pre0,table)==as_kana(pre1,table) is not None
        assert as_kana(post0,table)==as_kana(post1,table) is not None
        for variant,(pre,post) in enumerate(((pre0,post0),(pre1,post1))):
            raw=pre+w+post
            gold=(len(pre),len(pre)+len(w))
            assert raw[gold[0]:gold[1]]==w
            positive.append({'raw':raw,'gold':gold,'word':w,'variant':variant,'pair_id':i})
    for i,w in enumerate(negative_words):
        pre0,pre1,post0,post1=ALIASES[i%len(ALIASES)]
        for variant,(pre,post) in enumerate(((pre0,post0),(pre1,post1))):
            negative.append({'raw':pre+w+post,'variant':variant,'pair_id':i})
    return positive,negative


def score_boundary(edge,raw,cov):
    """Score endpoints using only input and candidate span, never gold."""
    st,en=edge[:2]
    left,right=raw[:st],raw[en:]
    if not left or not right:
        return -0.6
    lc,rc=cov(left),cov(right)
    complete=(1.0 if lc>0.99999 else -0.5)+(1.0 if rc>0.99999 else -0.5)
    before=1.0-cov(left+raw[st])
    after=1.0-cov(raw[en-1]+right)
    return .45*complete+.5*(before+after)


def h6_score(e,raw,cov):
    return score(e,RECIPES['char_phonotactic_boundary'])+0.9*score_boundary(e,raw,cov)


def h6_select(edges,raw,cov,threshold,cap=4):
    out=[e for e in edges if e[4] or h6_score(e,raw,cov)>threshold]
    out.sort(key=lambda e:(h6_score(e,raw,cov)+(.3 if e[4] else 0),e[1]-e[0],-e[0]),reverse=True)
    return out[:cap] if cap is not None else out


def h6_calibrate(negatives,cov,budget=.05):
    allow=math.floor(len(negatives)*budget)
    fixed=sum(any(e[4] for e in edges) for _,edges in negatives)
    if fixed>allow:
        return None
    maxima=sorted([max((h6_score(e,raw,cov) for e in edges),default=-1e6)
       for raw,edges in negatives if not any(e[4] for e in edges)],reverse=True)
    rem=allow-fixed
    threshold=maxima[rem] if rem<len(maxima) else -1e6
    assert sum(bool(h6_select(edges,raw,cov,threshold)) for raw,edges in negatives)<=allow
    return threshold


def metrics(positives,negatives,choose):
    tps=fps=fns=0
    exact=at_one=present=oracle=0
    start_errors=[];end_errors=[];errors=[]
    variants={}
    for c in positives:
        out=choose(c['raw'])
        gold=set(c['gold'])
        predicted=set(out[0][:2]) if out else set()
        tps+=len(predicted&gold);fps+=len(predicted-gold);fns+=len(gold-predicted)
        exact_flag=predicted==gold
        exact+=exact_flag
        at_one+=bool(out) and all(abs(out[0][j]-c['gold'][j])<=1 for j in (0,1))
        present+=bool(out)
        oracle+=any(tuple(e[:2])==c['gold'] for e in out)
        variants.setdefault(c['pair_id'],{})[c['variant']]=exact_flag
        if out:
            start_errors.append(abs(out[0][0]-c['gold'][0]))
            end_errors.append(abs(out[0][1]-c['gold'][1]))
        if not exact_flag and len(errors)<9:
            errors.append({'input':c['raw'],'gold':list(c['gold']),
                           'rank1':list(out[0][:2]) if out else None,
                           'gold_in_top4':any(tuple(e[:2])==c['gold'] for e in out)})
    negative_bad=0
    for c in negatives:
        out=choose(c['raw'])
        if out:
            negative_bad+=1
            fps+=len(set(out[0][:2]))
    precision=tps/(tps+fps) if tps+fps else 0
    recall=tps/(tps+fns) if tps+fns else 0
    return {'positive_cases':len(positives),'negative_cases':len(negatives),
      'candidate_top4_gold':oracle,'rank1_exact_span':exact,'rank1_present':present,
      'boundaries_tp':tps,'boundaries_fp':fps,'boundaries_fn':fns,
      'boundary_precision':round(precision,4),'boundary_recall':round(recall,4),
      'boundary_f1':round(2*precision*recall/(precision+recall),4) if precision+recall else 0,
      'rank1_both_within_one_char':at_one,
      'start_mae_when_selected':round(statistics.mean(start_errors),3) if start_errors else None,
      'end_mae_when_selected':round(statistics.mean(end_errors),3) if end_errors else None,
      'japanese_only_with_false_english':negative_bad,
      'alias_pairs_both_exact':sum(v.get(0)==v.get(1)==True for v in variants.values()),
      'alias_pairs_both_same_correctness':sum(v.get(0)==v.get(1) for v in variants.values()),
      'alias_pairs_total':len(variants),
      'errors':errors}


def run(root,seed):
    context=construct_models(root,seed)
    mapping=romaji_table(root)
    cov=phonology(load_kana_tokens(root))
    pos,neg=prepare_alias_corpus(context['oov'],context['jtest']+context['phon'],mapping)
    dev_neg=japanese_negative_streams(context['jdev'],size=100)
    neg_dev=[(r,decorate(r,score_edges(r,context['diff']),cov)) for r in dev_neg]
    h5w=RECIPES['char_phonotactic_boundary']
    h5th=calibrate([x for _,x in neg_dev],h5w,.05)
    h6th=h6_calibrate(neg_dev,cov,.05)
    if h5th is None or h6th is None:raise ValueError('Calibration failed')
    @lru_cache(maxsize=4096)
    def edges(raw):return decorate(raw,score_edges(raw,context['diff']),cov)
    def h5(raw):return selected(edges(raw),h5w,h5th,4)
    def h6(raw):return h6_select(edges(raw),raw,cov,h6th,4)
    baseline_positive=[{'raw':raw,'gold':gold,'word':w,'variant':0,'pair_id':i}
       for i,(raw,gold) in enumerate(english_streams(context['oov']))
       for w in [context['oov'][i]]]
    baseline_negative=[{'raw':raw,'variant':0,'pair_id':i}
       for i,raw in enumerate(japanese_negative_streams(context['jtest']+context['phon'],size=130))]
    legacy={'h5':metrics(baseline_positive,baseline_negative,h5),
            'h6':metrics(baseline_positive,baseline_negative,h6)}
    return {'seed':seed,'h5_development_threshold':round(h5th,5),
      'h6_development_threshold':round(h6th,5),
      'h5':metrics(pos,neg,h5),'h6':metrics(pos,neg,h6),'original_h5_style_holdout':legacy,
      'limitations':('Synthetic pair corpus and held-out Japanese types; '
          'not a natural-text gold corpus or integrated C# IME evaluation')}


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--root',type=Path,default=Path('.'))
    p.add_argument('--seeds',nargs='+',type=int,default=[20261010,20261011,20261012,20261013,20261014])
    p.add_argument('--output',type=Path)
    args=p.parse_args()
    data={'study':'H6 phonetic alias invariance + exact boundaries',
      'metrics':'language-transition boundaries of rank-1 English span (and JP-only negatives)',
      'records':[run(args.root,s) for s in args.seeds]}
    out=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
    if args.output:args.output.write_text(out,encoding='utf8')
    print(json.dumps({'records':[{'seed':x['seed'],
      'h5':{k:v for k,v in x['h5'].items() if k not in ('errors',)},
      'h6':{k:v for k,v in x['h6'].items() if k not in ('errors',)}} for x in data['records']]},
        ensure_ascii=False,indent=2))

if __name__=='__main__':
    main()
