#!/usr/bin/env python3
"""Protect benchmark correctness and provider budgeting boundaries."""
import copy
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]

def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'eng' / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value

benchmark = module('csv_benchmark', 'run-csvhelper-streaming.py')
advice = module('csv_advice', 'evaluate-csvhelper-advice.py')


def fixture():
    samples = []
    for count in (10000, 100000):
        digest = dict(Accepted=count, Rejected=0, IssueCount=0, Stop=0, StopRecord=0,
                      IdSum=count * (count + 1) // 2, AmountSum=count * (count + 1) // 2 / 100,
                      ActiveCount=count // 2, NoteCharacters=count * 8, Fingerprint=42)
        for mode in ('direct', 'functional'):
            for iteration in range(-2, 5):
                samples.append(dict(mode=mode, rows=count, iteration=iteration, warmup=iteration < 0,
                                    elapsedMilliseconds=1, allocatedBytes=1000, digest=dict(digest)))
    return dict(schema='csv-stream-benchmark/0.1', executionStatus='completed', samples=samples,
                runtime='fixture', os='fixture', scope='failure-boundary fixture', tieredCompilation='0', buildConfiguration='Release')


class StreamingEvidenceTests(unittest.TestCase):
    def test_missing_samples_are_rejected(self):
        raw = fixture(); raw['samples'].pop()
        with self.assertRaises(ValueError): benchmark.validate(raw)

    def test_duplicate_run_cannot_replace_missing_run(self):
        raw = fixture(); raw['samples'][0] = copy.deepcopy(raw['samples'][1])
        with self.assertRaises(ValueError): benchmark.validate(raw)

    def test_wrong_output_digest_invalidates_timing(self):
        raw = fixture(); raw['samples'][0]['digest']['IdSum'] += 1
        with self.assertRaises(ValueError): benchmark.validate(raw)

    def test_equal_totals_do_not_hide_different_notes(self):
        raw = fixture(); raw['samples'][0]['digest']['Fingerprint'] += 1
        with self.assertRaises(ValueError): benchmark.validate(raw)

    def test_nonfinite_or_zero_timing_is_rejected(self):
        for duration in (0, float('nan'), float('inf'), True):
            raw = fixture(); raw['samples'][0]['elapsedMilliseconds'] = duration
            with self.subTest(duration=duration), self.assertRaises(ValueError): benchmark.validate(raw)

    def test_missing_content_digest_is_rejected(self):
        raw = fixture(); del raw['samples'][0]['digest']['Fingerprint']
        with self.assertRaises(ValueError): benchmark.validate(raw)

    def test_provider_requires_explicit_live(self):
        with self.assertRaises(ValueError): advice.validate_request(False, ['openai'], 6, 1)

    def test_provider_budget_checked_before_any_call(self):
        with self.assertRaises(ValueError): advice.validate_request(True, ['openai', 'jev'], 5, 2)

    def test_duplicate_provider_is_rejected(self):
        with self.assertRaises(ValueError): advice.validate_request(True, ['openai', 'openai'], 6, 2)

    def test_offline_comparison_needs_no_model(self):
        advice.validate_request(False, [], 6, 0)


if __name__ == '__main__':
    unittest.main()
