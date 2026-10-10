import importlib.util
import pathlib
import random
import re
import cmudict

root = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('oov', pathlib.Path(__file__).with_name('oov_character_model_experiment.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
ja, oov, phon = module.corpus(root)
lex = (root / 'src/BoundaryLab.Core/Lexicon.cs').read_text(encoding='utf-8')
eblock = lex.split('var e = ')[1]
known_english = set(re.findall(r'\("([a-z]+)",\s*"[^\"]+",\s*[0-9.]+,\s*"english-[^\"]+"\)', eblock))
print('japanese_lexicon', len(ja), 'oov_snapshot', len(oov), 'phonetic_holdout', len(phon))
print('oov_in_english_lexicon', sorted(set(oov) & known_english))
print('oov_in_japanese_lexicon', sorted(set(oov) & set(ja)))
print('phon_in_japanese_lexicon', sorted(set(phon) & set(ja)))
for seed in range(20261010, 20261015):
    rng = random.Random(seed)
    shuffled = ja[:]
    rng.shuffle(shuffled)
    jtrain, jdev, jtest = shuffled[:55], shuffled[55:70], shuffled[70:]
    stop = set(ja + phon + oov + ['japanese','tokyo','ime','node','monitor','customer'])
    english = sorted(set(w.lower() for w in cmudict.words() if w.isascii() and w.isalpha() and 3 <= len(w) <= 24 and w.lower() not in stop))
    rng.shuffle(english)
    edev, etrain = english[:450], english[450:]
    print(seed, 'English train/dev', len(etrain), len(edev),
          'English eval/training overlap', len(set(etrain) & (set(oov)|set(ja)|set(phon))),
          'English eval/dev overlap', len(set(edev) & (set(oov)|set(ja)|set(phon))),
          'Japanese train/test overlap', len(set(jtrain) & set(jtest)),
          'Japanese dev/test overlap', len(set(jdev) & set(jtest)),
          'Japanese train/English OOV overlap', len(set(jtrain) & set(oov)))
