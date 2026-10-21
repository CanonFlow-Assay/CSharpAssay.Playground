#!/usr/bin/env python3
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('roundtrip', Path(__file__).with_name('run-csvhelper-roundtrip.py'))
roundtrip = importlib.util.module_from_spec(spec)
spec.loader.exec_module(roundtrip)


def worker():
    fixtures = []
    samples = []
    for identifier, culture in [('roundtrip-invariant', ''), ('roundtrip-german', 'de-DE')]:
        fixtures.append({'Specification': dict(Id=identifier, Rows=50_000, NoteCharacters=128, Unicode=True, Culture=culture, InvalidEvery=0), 'Expected': {'sentinel': 'complete'}})
        for pipeline in ('Original', 'Assayed'):
            for iteration in (-1, 0, 1, 2):
                samples.append(dict(caseId=identifier, pipeline=pipeline, iteration=iteration, warmup=iteration < 0, digest={'sentinel': 'complete'}, elapsedMilliseconds=10, allocatedBytes=1000, outputBytes=100, outputSha256='A' * 64))
    return dict(schema='csv-roundtrip-workload/0.1', executionStatus='completed', tieredCompilation='1', tieredPgo='1', fixtures=fixtures, samples=samples)


class RoundTripBoundaryTests(unittest.TestCase):
    def test_missing_run(self):
        value = worker(); value['samples'].pop()
        with self.assertRaises(ValueError): roundtrip.validate(value)

    def test_duplicate_run(self):
        value = worker(); value['samples'][1] = copy.deepcopy(value['samples'][2])
        with self.assertRaises(ValueError): roundtrip.validate(value)

    def test_changed_output_bytes_with_equal_values(self):
        value = worker(); value['samples'][0]['outputSha256'] = 'B' * 64
        with self.assertRaises(ValueError): roundtrip.validate(value)

    def test_incomplete_or_invalid_output(self):
        for field, replacement in [('digest', {'sentinel': 'partial'}), ('elapsedMilliseconds', float('nan'))]:
            value = worker(); value['samples'][0][field] = replacement
            with self.subTest(field=field), self.assertRaises(ValueError): roundtrip.validate(value)


if __name__ == '__main__':
    unittest.main()
