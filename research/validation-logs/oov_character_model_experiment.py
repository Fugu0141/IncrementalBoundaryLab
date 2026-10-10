#!/usr/bin/env python3
"""Holdout test: does an independent English character model expose OOV Latin word candidates?

This is a *candidate-level* diagnostic, NOT an evaluation of the C# stream decoder.
Fixed corpus snapshots allow running this file on the repository main branch.
Requires `pip install cmudict`; optional --root points to extracted repository.
No training word equals a held-out Japanese/English evaluation word.
"""
import argparse, collections, json, math, random, re
from pathlib import Path
import cmudict

# Fixed token snapshots from the unattached evaluation-harness commit 23e1f6b.
# These enable identical offline H1 evaluation on the repository main branch.
SNAPSHOT_OOV = "knowledge strategy performance schedule feedback actually probably basically weekend meeting project deadline release update review document customer feature requirement database security backup laptop keyboard monitor printer wireless calendar holiday airport hospital restaurant library teacher weather morning evening already quickly slowly carefully suddenly yesterday together important difficult possible available necessary beautiful wonderful terrible awesome amazing".split()
SNAPSHOT_PHON = "kuru iku matsu aruku hanasu yomu kaku miseru akeru shimeru oyogu hashiru tsukuru nomu kiku utau odoru warau naku".split()

SEED = 20261010
ENDINGS = ('ology','tion','sion','ment','ness','able','ible','ese')

def corpus(root):
    lex=(root/'src/BoundaryLab.Core/Lexicon.cs').read_text(encoding='utf-8')
    jblock=lex.split('var j = ')[1].split('var e = ')[0]
    ja=list(dict.fromkeys(re.findall(r'\("([a-z]+)",\s*"[^\"]+",\s*[0-9.]+,\s*"japanese-[^\"]+"\)',jblock)))
    corpus_file=root/'tests/BoundaryLab.Core.EvalHarness/Corpus.cs'
    if corpus_file.is_file():
        src=corpus_file.read_text(encoding='utf-8')
        oblock=src.split('private static readonly EvalToken[] EnOov = new[]')[1].split('}.Select')[0]
        oov=re.findall(r'"([a-z]+)"',oblock)
        pblock=src.split('JaPhoneticOnly =')[1].split('];')[0]
        phon=re.findall(r'new\("([a-z]+)"',pblock)
        if oov != SNAPSHOT_OOV or phon != SNAPSHOT_PHON:
            raise ValueError('Evaluation corpus differs from the recorded study snapshot')
    else:
        oov=SNAPSHOT_OOV
        phon=SNAPSHOT_PHON
    return ja,oov,phon

class Markov:
    # Interpolated variable-order character Markov conditional log-likelihood,
    # estimated by count frequencies and a fixed smoothing constant.
    def __init__(self, words, n=3):
        self.counts=collections.Counter()
        self.totals=collections.Counter()
        for w in words:
            s='^'+w+'$'
            for i in range(1,len(s)):
                for z in range(1, min(n,i+1)+1):
                    gr=s[i-z+1:i+1]
                    self.counts[gr]+=1
                    self.totals[gr[:-1]]+=1
        self.n=n

    def likelihood(self,w):
        s='^'+w+'$';ll=0.0
        for i in range(1,len(s)):
            gram=s[max(0,i-self.n+1):i+1]
            while len(gram)>1 and gram[:-1] not in self.totals:
                gram=gram[1:]
            ll+=math.log((self.counts[gram]+.25)/(self.totals[gram[:-1]]+.25*27))
        return ll/(len(s)-1)

def confusion(pred, pos, neg):
    tp=sum(pred(w) for w in pos);fp=sum(pred(w) for w in neg)
    return {'positive':len(pos),'positive_detected':tp,'recall':round(tp/len(pos),4),
            'negative':len(neg),'negative_detected':fp,'false_positive_rate':round(fp/len(neg),4)}

def run(root, seed=SEED):
    ja,oov,phon=corpus(root)
    rng=random.Random(seed)
    shuffled=ja[:];rng.shuffle(shuffled)
    jtrain=shuffled[:55];jdev=shuffled[55:70];jtest=shuffled[70:]
    stop=set(ja+phon+oov+['japanese','tokyo','ime','node','monitor','customer'])
    english=sorted(set(w.lower() for w in cmudict.words() if w.isascii() and w.isalpha() and 3<=len(w)<=24 and w.lower() not in stop))
    rng.shuffle(english)
    edev=english[:450];etrain=english[450:]
    jmodel=Markov(jtrain);emodel=Markov(etrain)
    def diff(w):return emodel.likelihood(w)-jmodel.likelihood(w)
    # Pre-specified threshold selection using ONLY dev vocabulary, with >=95% specificity.
    vals=sorted(set([round(diff(w),5) for w in jdev+edev]+[0.0]))
    eligible=[]
    for th in vals:
        fp=sum(diff(w)>th for w in jdev)
        if fp<=math.floor(len(jdev)*.05):
            recall=sum(diff(w)>th for w in edev)/len(edev)
            eligible.append((recall,-th,th))
    threshold=max(eligible)[-1]
    shape=lambda w: len(w)>=7 and len(w)<=24 and w.endswith(ENDINGS)
    model=lambda w: diff(w)>threshold
    neg=jtest+phon
    both=lambda w: shape(w) or model(w)
    benchmarks={'suffix_morphology':confusion(shape,oov,neg),
                'character_model':confusion(model,oov,neg),
                'hybrid_candidate_union':confusion(both,oov,neg)}
    # This deliberately violates the word-level assumption: a Japanese-only
    # multiword stream is easily mistaken for one giant Latin token.
    multi_ja=[]
    suffixes=('ha','ga','no','ni','de','to','mo')
    for i in range(100):
        t1=neg[(i*7)%len(neg)];t2=neg[(i*11+3)%len(neg)]
        chunk=t1+suffixes[i%len(suffixes)]+t2
        if 7<=len(chunk)<=24: multi_ja.append(chunk)
    benchmarks['japanese_only_glued_controls']={
        'n':len(multi_ja),
        'morphology_false_candidates':sum(shape(w) for w in multi_ja),
        'model_false_candidates':sum(model(w) for w in multi_ja),
        'hybrid_false_candidates':sum(both(w) for w in multi_ja),
        'examples':[w for w in multi_ja if both(w)][:12]
    }
    # Full-stream candidate *generation* stress: do not confuse isolated
    # classification accuracy with identifying a substring inside unspaced text.
    particles=['ha','ga','wo','ni','de','to','mo','no','yo']
    def candidates(raw, test):
        out=[]
        for start in range(len(raw)):
            for end in range(start+7,min(len(raw),start+24)+1):
                span=raw[start:end]
                if not span.isascii() or not span.isalpha():continue
                if end < len(raw) and not any(raw.startswith(p,end) for p in particles):continue
                if test(span): out.append((start,end))
        return out
    positives=[]
    for i,w in enumerate(oov):
        pre=('kyouha','korega','watashino','imanode')[i%4]
        post=particles[i%len(particles)]
        raw=pre+w+post
        positives.append((raw,(len(pre),len(pre)+len(w))))
    negative_controls=[]
    for i,w in enumerate(neg):
        pre=('kyouha','korega','watashino','imanode')[i%4]
        post=particles[i%len(particles)]
        negative_controls.append(pre+w+post)
    localized={}
    for name,detect in [('morphology',shape),('character_model',model),('hybrid',both)]:
        outputs=[candidates(raw,detect) for raw,gold in positives]
        negative_outputs=[candidates(raw,detect) for raw in negative_controls]
        localized[name]={
            'positive_cases':len(outputs),
            'positive_gold_span_recovered':sum(gold in candidateset for (_,gold),candidateset in zip(positives,outputs)),
            'total_proposed_spans_positive_cases':sum(len(x) for x in outputs),
            'negative_cases':len(negative_outputs),
            'negative_cases_with_any_false_english_span':sum(bool(x) for x in negative_outputs),
            'total_false_spans_negative_cases':sum(len(x) for x in negative_outputs),
        }
    benchmarks['mixed_stream_candidate_recall_and_spurious_spans']=localized
    benchmarks['japanese_test_count']=len(jtest)
    benchmarks['japanese_phonetic_holdout_count']=len(phon)
    benchmarks['english_dev_count']=len(edev)
    benchmarks['japanese_dev_count']=len(jdev)
    benchmarks['english_train_count']=len(etrain)
    benchmarks['japanese_train_count']=len(jtrain)
    benchmarks['threshold']=round(threshold,5)
    benchmarks['split_seed']=seed
    benchmarks['paradox_probes']={w:{'english_minus_japanese':round(diff(w),3),
                                     'morphology':shape(w),'model_candidate':model(w)}
                                    for w in ['japanese','tokyo','ime','node','monitor','customer','strategy']}
    benchmarks['english_missed_by_model']=[w for w in oov if not model(w)]
    benchmarks['japanese_false_positives_model']=[w for w in neg if model(w)]
    benchmarks['english_detected_model_only']=[w for w in oov if model(w) and not shape(w)]
    benchmarks['english_detected_suffix_only']=[w for w in oov if shape(w) and not model(w)]
    # Exact paired discordance test: (only model) vs (only morphology).
    a=sum(model(w) and not shape(w) for w in oov)
    b=sum(shape(w) and not model(w) for w in oov)
    from math import comb
    total=a+b
    two_sided=min(1.0, 2*sum(comb(total,k) for k in range(min(a,b)+1))/(2**total))
    benchmarks['paired_candidate_comparison']={'model_only':a,'morphology_only':b,
                                                'exact_mcnemar_p_two_sided':two_sided}
    particles=['ha','ga','wo','ni','de','to','mo','no','yo']
    collisions={w:[p for p in particles if p in w] for w in oov}
    benchmarks['oov_particle_collisions']={'count':sum(bool(v) for v in collisions.values()),
         'fraction':round(sum(bool(v) for v in collisions.values())/len(oov),4),
         'examples':{k:v for k,v in collisions.items() if v}}
    return benchmarks

if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('--root',default='.',type=Path)
    a.add_argument('--output',type=Path)
    a.add_argument('--seed',type=int,default=SEED)
    args=a.parse_args(); data=run(args.root,args.seed)
    result=json.dumps(data,ensure_ascii=False,indent=2)
    if args.output:args.output.write_text(result+'\n',encoding='utf-8')
    print(result)