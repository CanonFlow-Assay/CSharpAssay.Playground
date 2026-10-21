#!/usr/bin/env python3
"""Failure-boundary tests for CsvHelper execution evidence, without providers."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
csv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(csv)


class EvidenceTests(unittest.TestCase):
    def test_mutation_requires_exactly_one_span(self):
        case = {'before': 'value', 'after': 'other', 'id': 'case'}
        for text in ('absent', 'value value'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                csv.exact_edit(text, case)

    def test_zero_test_execution_cannot_pass(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'tests.xml'
            report.write_text('<assemblies><assembly total="0" passed="0" failed="0" skipped="0" errors="0" /></assemblies>')
            with self.assertRaises(ValueError):
                csv.test_record(report, 0, 79)

    def test_failed_process_cannot_reuse_passing_report(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'tests.xml'
            report.write_text('<assemblies><assembly total="1" passed="1" failed="0" skipped="0" errors="0"><collection><test name="old" result="Pass" /></collection></assembly></assemblies>')
            with self.assertRaises(ValueError):
                csv.test_record(report, 2, 1)

    def test_partial_test_selection_cannot_pass(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'tests.xml'
            report.write_text('<assemblies><assembly total="79" passed="79" failed="0" skipped="0" errors="0"><collection><test name="only-one" result="Pass" /></collection></assembly></assemblies>')
            with self.assertRaises(ValueError):
                csv.test_record(report, 0, 79)

    def test_coverage_cannot_omit_adapter(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'coverage.xml'
            report.write_text('<coverage lines-covered="1" lines-valid="1" branches-covered="1" branches-valid="1"><packages><package name="CsvFunctional.Domain" /></packages></coverage>')
            with self.assertRaises(ValueError):
                csv.coverage_record(report)

    def test_empty_coverage_cannot_pass(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'coverage.xml'
            report.write_text('<coverage lines-covered="0" lines-valid="0" branches-covered="0" branches-valid="0"><packages><package name="CsvFunctional.Domain"/><package name="CsvFunctional.Adapter"/></packages></coverage>')
            with self.assertRaises(ValueError):
                csv.coverage_record(report)

    def test_stale_success_file_is_deleted_before_execution(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder)
            (target / 'tests.xml').write_text('stale')
            with patch.object(csv, 'run', return_value=type('Result', (), {'returncode': 1})()):
                with self.assertRaises(FileNotFoundError):
                    csv.tests(target, target, 79)
            self.assertFalse((target / 'tests.xml').exists())

    def test_changed_input_binding_rejects_completion(self):
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'input.cs'
            target.write_text('original')
            binding = {str(target): csv.sha(target)}
            target.write_text('changed')
            with self.assertRaises(ValueError):
                csv.assert_bindings(binding)


if __name__ == '__main__':
    unittest.main()
