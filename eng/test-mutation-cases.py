#!/usr/bin/env python3
"""Failure-boundary tests for the executed mutation corpus; no compiler or models."""
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('mutation_cases', Path(__file__).with_name('run-decision-assistance.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MutationExecutionTests(unittest.TestCase):
    def result(self, code, output):
        return subprocess.CompletedProcess(['fixture'], code, output, '')

    def test_zero_tests_cannot_masquerade_as_a_passing_selected_suite(self):
        with self.assertRaises(ValueError):
            module.probe_observation(self.result(0, '{"tests":0,"failures":0,"policyCalls":0}'),
                                     {'tests': 2, 'policyCalls': 2})

    def test_disagreeing_exit_code_is_not_a_survivor(self):
        with self.assertRaises(ValueError):
            module.probe_observation(self.result(0, '{"tests":2,"failures":1,"policyCalls":2}'),
                                     {'tests': 2, 'policyCalls': 2})

    def test_boolean_counts_are_rejected(self):
        with self.assertRaises(ValueError):
            module.probe_observation(self.result(0, '{"tests":true,"failures":0,"policyCalls":1}'),
                                     {'tests': 1, 'policyCalls': 1})

    def test_probe_crash_is_not_a_killed_mutant(self):
        with self.assertRaises(RuntimeError):
            module.probe_observation(self.result(137, ''), {'tests': 2, 'policyCalls': 2})

    def test_build_or_restore_failure_is_not_an_invalid_mutation(self):
        for result in (self.result(1, 'error NU1301: unavailable'), self.result(137, ''),
                       self.result(1, 'Build failed without identified diagnostics')):
            with self.subTest(code=result.returncode, output=result.stdout):
                with self.assertRaises(RuntimeError):
                    module.compilation(result)

    def test_failing_original_stops_before_mutant_build(self):
        case = {'id': 'baseline', 'selectedSuite': 'broken', 'replacement': 'amount >= 0', 'expectedBaselineFailures': 1}
        with patch.object(module, 'observe', return_value={'tests': 1, 'failures': 1, 'policyCalls': 1, 'exitCode': 7}), patch.object(module, 'compile_probe') as compile_probe:
            record = module.execute_case(case, {'broken': {'hasBoundaryAssertion': False}},
                                         Path('original'), Path('scratch'), b'amount > 0')
        compile_probe.assert_not_called()
        self.assertEqual(record['outcome'], 'baseline_failed')
        self.assertFalse(record['testsCompleted'])
        self.assertFalse(record['survived'])

    def test_unrelated_compiler_error_is_not_the_expected_invalid_mutation(self):
        case = {'id': 'invalid', 'selectedSuite': 'weak', 'replacement': 'amount >', 'expectedDiagnostic': 'CS1525', 'expectedBaselineFailures': 0}
        with patch.object(module, 'observe', return_value={'tests': 2, 'failures': 0, 'policyCalls': 2, 'exitCode': 0}), patch.object(module, 'compile_probe', return_value={'status': 'invalid', 'exitCode': 1, 'diagnostics': ['CS9999']}):
            with self.assertRaises(RuntimeError):
                module.execute_case(case, {'weak': {'hasBoundaryAssertion': False}},
                                    Path('original'), Path('scratch'), b'amount > 0')

    def test_timeout_replaces_a_stale_success_report_with_failed_run(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            report = output / 'mutation-cases.json'
            report.write_text('{"executionStatus":"completed"}')
            with patch.object(module, 'OUTPUT', output), patch.object(module, 'ROOT', output), patch.object(module, 'command', side_effect=subprocess.TimeoutExpired('dotnet', 120)), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(subprocess.TimeoutExpired):
                    module.main()
            self.assertEqual(json.loads(report.read_text())['executionStatus'], 'failed')


if __name__ == '__main__':
    unittest.main()
