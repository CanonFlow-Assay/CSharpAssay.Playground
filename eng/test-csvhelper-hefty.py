#!/usr/bin/env python3
"""Failure boundaries for heavyweight physical-file comparisons."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('csv_hefty', ROOT / 'eng/run-csvhelper-hefty.py')
hefty = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hefty)


def fixture():
    result = {'Totals': dict(Accepted=20, Rejected=0, IdSum=210, AmountSum=2.1, ActiveCount=10,
                             NoteCharacters=320, Fingerprint=42, IssueCount=0, Stop=0, StopRecord=0),
              'OutputSha256': 'A' * 64, 'Failure': 0}
    return {'Specification': dict(Id='small', Rows=20, NoteCharacters=16, Unicode=False, Culture='', InvalidEvery=0),
            'FileName': 'small.csv', 'Expected': result, 'OriginalExpected': copy.deepcopy(result)}


def child():
    value = fixture()
    return dict(schema='csv-hefty-child/0.1', executionStatus='completed', caseId='small', pipeline='Original',
                tieredCompilation='1', tieredPgo='1', buildConfiguration='Release', runtime='fixture', os='fixture', peakWorkingSetBytes=1024,
                samples=[dict(iteration=i, warmup=i < 0, elapsedMilliseconds=10, allocatedBytes=1000,
                              result=copy.deepcopy(value['Expected']), equivalentToCompleteExpected=True, gen0=0, gen1=0, gen2=0) for i in range(-2, 3)])


class HeavyEvidenceTests(unittest.TestCase):
    def test_missing_measured_run_is_rejected(self):
        raw = child(); raw['samples'].pop()
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_duplicate_cannot_replace_a_run(self):
        raw = child(); raw['samples'][0] = copy.deepcopy(raw['samples'][1])
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_equal_counts_do_not_hide_corrupt_content(self):
        raw = child(); raw['samples'][0]['result']['OutputSha256'] = 'B' * 64
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_partial_output_cannot_claim_complete_equivalence(self):
        raw = child(); raw['samples'][0]['equivalentToCompleteExpected'] = False
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_wrong_runtime_mode_is_rejected(self):
        raw = child(); raw['tieredCompilation'] = '0'
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_missing_peak_memory_context_is_rejected(self):
        raw = child(); raw['peakWorkingSetBytes'] = 0
        with self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_invalid_measurements_are_rejected(self):
        for value in (0, float('nan'), True):
            raw = child(); raw['samples'][0]['elapsedMilliseconds'] = value
            with self.subTest(value=value), self.assertRaises(ValueError): hefty.validate_child(raw, fixture(), 'Original', 'tiered-pgo')

    def test_changed_physical_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder); path = directory / 'small.csv'; path.write_bytes(b'fixture bytes')
            item = fixture(); item.update(FileBytes=path.stat().st_size, FileSha256=hefty.file_sha(path))
            manifest = dict(Schema='csv-hefty-fixtures/0.1', Fixtures=[item])
            plan = dict(cases=[dict(id='small', rows=20, noteCharacters=16, unicode=False, culture='', invalidEvery=0)], maximumTotalFileBytes=1024)
            hefty.validate_manifest(manifest, plan, directory)
            path.write_bytes(b'changed bytes')
            with self.assertRaises(ValueError): hefty.validate_manifest(manifest, plan, directory)


if __name__ == '__main__':
    unittest.main()
