#!/usr/bin/env python3
"""Evaluate manually verified IME outputs without treating heuristics as probabilities.

Usage (run at repository root):
  python research/evaluate.py --corpus research/pilot-corpus.jsonl \
    --observations research/observations.jsonl \
    --baseline v0.6_offline --candidate v0.6_mozc

No score is produced for gold_status != "verified". All conditions use the
same pre-annotated gold. This does NOT run Mozc or benchmark input latency.
"""
import argparse
import json
import random
import sys
from pathlib import Path


def read_jsonl(path):
    result = []
    with Path(path).open(encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{line_number}: invalid JSON: {exc}") from exc
            if not isinstance(obj, dict):
                raise ValueError(f"{path}:{line_number}: expected JSON object")
            result.append(obj)
    return result


def edit_distance(a, b):
    """Unicode code-point Levenshtein distance; NFC normalisation is NOT implicit."""
    previous = list(range(len(b) + 1))
    for i, ac in enumerate(a, 1):
        current = [i]
        for j, bc in enumerate(b, 1):
            current.append(min(
                previous[j] + 1, current[-1] + 1,
                previous[j - 1] + (ac != bc)))
        previous = current
    return previous[-1]


def boundary_counts(gold, predicted):
    g, p = set(gold), set(predicted)
    return len(g & p), len(p - g), len(g - p)


def evaluate(corpus, observations):
    references = {}
    for row in corpus:
        key = row.get("id")
        if not isinstance(key, str) or not key or key in references:
            raise ValueError(f"missing or duplicate corpus ID: {key!r}")
        references[key] = row

    records = {}
    ignored = 0
    for obs in observations:
        if obs.get("status") == "template":
            ignored += 1
            continue
        case_id, condition = obs.get("case_id"), obs.get("condition")
        if case_id not in references:
            raise ValueError(f"unknown case_id: {case_id!r}")
        if not isinstance(condition, str) or not condition:
            raise ValueError(f"{case_id}: missing condition")
        key = (condition, case_id)
        if key in records:
            raise ValueError(f"duplicate observation {key}; use distinct case IDs for repeats")

        gold = references[case_id]
        if gold.get("gold_status") != "verified":
            ignored += 1
            continue
        expected = gold.get("gold_output")
        if not isinstance(expected, str):
            raise ValueError(f"{case_id}: verified gold_output must be a string")

        value = obs.get("observed_output")
        file_ref = obs.get("research_json")
        if file_ref:
            artifact = json.loads(Path(file_ref).read_text(encoding="utf-8"))
            if artifact.get("input") != gold.get("raw", "").lower():
                raise ValueError(f"{key}: research JSON input differs from corpus raw")
            from_file = artifact.get("output")
            if not isinstance(from_file, str):
                raise ValueError(f"{key}: research JSON has no output")
            if value is not None and value != from_file:
                raise ValueError(f"{key}: observed_output differs from saved research JSON")
            value = from_file
        if not isinstance(value, str):
            raise ValueError(f"{key}: missing observed_output or research_json")

        reference_boundaries = gold.get("gold_boundaries")
        predicted_boundaries = obs.get("predicted_boundaries")
        bstats = None
        if reference_boundaries is not None and predicted_boundaries is not None:
            raw_len = len(gold["raw"])
            for label, boundaries in (("gold", reference_boundaries),
                                      ("predicted", predicted_boundaries)):
                if (not isinstance(boundaries, list) or
                    any(type(i) is not int or not 0 < i < raw_len for i in boundaries) or
                    len(boundaries) != len(set(boundaries))):
                    raise ValueError(f"{key}: invalid {label} boundaries")
            bstats = boundary_counts(reference_boundaries, predicted_boundaries)

        records[key] = {
            "gold": expected,
            "observed": value,
            "distance": edit_distance(expected, value),
            "characters": len(expected),
            "exact": expected == value,
            "boundaries": bstats,
        }
    return records, ignored


def aggregate(rows):
    total_chars = sum(row["characters"] for row in rows)
    error_chars = sum(row["distance"] for row in rows)
    if total_chars <= 0:
        return None
    boundary_rows = [row["boundaries"] for row in rows if row["boundaries"] is not None]
    b_tp = sum(x[0] for x in boundary_rows)
    b_fp = sum(x[1] for x in boundary_rows)
    b_fn = sum(x[2] for x in boundary_rows)
    boundary_f1 = None
    if boundary_rows and 2 * b_tp + b_fp + b_fn > 0:
        boundary_f1 = 2 * b_tp / (2 * b_tp + b_fp + b_fn)
    return {
        "n": len(rows),
        "character_errors": error_chars,
        "reference_characters": total_chars,
        "cer": error_chars / total_chars,
        "exact_match": sum(row["exact"] for row in rows) / len(rows),
        "boundary_n": len(boundary_rows),
        "boundary_micro_f1": boundary_f1,
    }


def paired_delta(records, baseline, candidate, resamples, seed):
    common = sorted(
        {case for cond, case in records if cond == baseline} &
        {case for cond, case in records if cond == candidate})
    if not common:
        return None
    pairs = [(records[(baseline, case)], records[(candidate, case)])
             for case in common]
    if any(a["gold"] != b["gold"] for a, b in pairs):
        raise ValueError("paired observations must use identical gold")
    def delta(sample):
        denominator = sum(a["characters"] for a, _ in sample)
        if not denominator:
            raise ValueError("no reference characters")
        return (sum(a["distance"] - b["distance"] for a, b in sample)
                / denominator)
    point = delta(pairs)
    rng = random.Random(seed)
    values = sorted(delta([pairs[rng.randrange(len(pairs))]
                           for _ in pairs]) for _ in range(resamples))
    low = values[int(0.025 * (resamples - 1))]
    high = values[int(0.975 * (resamples - 1))]
    return {"matched_n": len(pairs), "delta_cer": point,
            "bootstrap_95ci": [low, high],
            "resamples": resamples, "seed": seed}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--corpus", required=True)
    p.add_argument("--observations", required=True)
    p.add_argument("--baseline", default="v0.6_offline")
    p.add_argument("--candidate", default="v0.6_mozc")
    p.add_argument("--resamples", type=int, default=5000)
    p.add_argument("--seed", type=int, default=20261009)
    args = p.parse_args()
    if args.resamples < 100:
        p.error("--resamples must be >= 100")
    try:
        records, ignored = evaluate(
            read_jsonl(args.corpus), read_jsonl(args.observations))
        if not records:
            print("No verified, measured observations yet. "
                  "Do not report performance gains; confirm gold first.")
            print(f"ignored templates/unverified observations: {ignored}")
            return 2
        conditions = sorted(set(condition for condition, _ in records))
        summary = {
            "method": "paired CER from pre-verified manually annotated gold",
            "ignored_unverified": ignored,
            "conditions": {
                condition: aggregate([entry for (c, _), entry in records.items()
                                      if c == condition])
                for condition in conditions
            },
            "paired_comparison": paired_delta(
                records, args.baseline, args.candidate,
                args.resamples, args.seed),
            "notes": [
                "CER uses Unicode code points; normalization is not automatic",
                "boundary F1 is reported only when both gold and predictions are supplied",
                "native TSF failures and latency are NOT measured by this script",
                "bootstrap CI describes sampling uncertainty, not universal correctness"
            ]
        }
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"evaluation error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
