#!/usr/bin/env python3
"""Regression checks for selected-test evidence and bounded live comparisons."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('shape_investigations', Path(__file__).with_name('run-shape-investigations.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ShapeEvidenceTests(unittest.TestCase):
    def record(self, total=1, passed=1, failed=0, skipped=0, errors=0):
        return f'<assemblies><assembly total="{total}" passed="{passed}" failed="{failed}" skipped="{skipped}" errors="{errors}"><collection><test name="test" result="Pass" /></collection></assembly></assemblies>'

    def test_zero_or_skipped_tests_never_establish_a_passing_baseline(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'tests.xml'
            for text in (self.record(total=0, passed=0), self.record(passed=0, skipped=1)):
                path.write_text(text)
                with self.assertRaises(ValueError):
                    module.test_record(path, 0, 1)

    def test_process_failure_cannot_be_reported_as_a_passing_suite(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'tests.xml'
            path.write_text(self.record())
            for code in (2, 137):
                with self.assertRaises(ValueError):
                    module.test_record(path, code, 1)

    def test_missing_selected_test_details_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'tests.xml'
            path.write_text(self.record(total=2, passed=2))
            with self.assertRaises(ValueError):
                module.test_record(path, 0, 2)

    def test_partial_coverage_scope_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / 'coverage.xml'
            path.write_text('<coverage><packages><package name="Shape.Domain" /></packages></coverage>')
            with self.assertRaises(ValueError):
                module.coverage_record(path)

    def test_stale_suite_artifact_cannot_establish_a_fresh_passing_run(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            (folder / 'tests.xml').write_text(self.record())
            with patch.object(module, 'run', return_value=subprocess.CompletedProcess(['fixture'], 0, '', '')):
                with self.assertRaises(FileNotFoundError):
                    module.tests(Path('sample'), folder, 1)

    def test_configured_key_does_not_enable_provider_without_live(self):
        with patch('sys.argv', ['run-shape-investigations', '--provider', 'jev']), patch.dict(module.os.environ, {'TYPESAFE_API_KEY': 'synthetic'}, clear=True), patch.object(module.subprocess, 'run') as run, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                module.main()
            run.assert_not_called()

    def test_absent_keys_are_reported_without_any_process_or_http_call(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(module, 'OUTPUT', Path(temporary)), patch.dict(module.os.environ, {}, clear=True), patch.object(module, 'advice') as advice:
            status = module.compare_providers([], ['jev', 'openai'], 8)
            advice.assert_not_called()
            report = json.loads((Path(temporary) / 'provider-comparison.json').read_text())
            self.assertEqual(status, 'unavailable_missing_keys')
            self.assertEqual(report['requestsAttempted'], 0)
            self.assertEqual(set(report['providers'].values()), {'unavailable_missing_key'})

    def test_budget_is_checked_before_any_live_process(self):
        with patch.dict(module.os.environ, {'TYPESAFE_API_KEY': 'synthetic', 'OPENAI_API_KEY': 'synthetic'}, clear=True), patch.object(module, 'advice') as advice:
            with self.assertRaises(ValueError):
                module.compare_providers([Path('a'), Path('b')], ['jev', 'openai'], 3)
            advice.assert_not_called()


if __name__ == '__main__':
    unittest.main()
