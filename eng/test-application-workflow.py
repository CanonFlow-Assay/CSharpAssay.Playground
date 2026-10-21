#!/usr/bin/env python3
"""Execution/evidence failure boundaries for the reusable application workflow."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parent))
from assay_workflow import runner
from assay_workflow.scaffold import create
from assay_workflow.application import coverage_observations


class ExecutionBoundaryTests(unittest.TestCase):
    def test_timeout_is_not_a_killed_mutant_or_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            r = runner.run([sys.executable, '-c', 'import time; time.sleep(10)'], directory, Path(directory) / 'log', .1)
            self.assertEqual('TimedOut', r.state)

    def test_absent_test_report_is_tool_failure(self):
        value = runner.test_record(Path('/nonexistent/assay-test.xml'), runner.Execution('Passed', 0, ''), 1)
        self.assertEqual('ToolFailure', value['state'])

    def test_zero_tests_cannot_pass(self):
        self.check_report('<assemblies><assembly total="0" passed="0" failed="0"/></assemblies>', 'ToolFailure')

    def test_skipped_tests_cannot_pass(self):
        self.check_report('<assemblies><assembly total="1" passed="0" failed="0" skipped="1"><test name="fixture" result="Skip"/></assembly></assemblies>', 'ToolFailure')

    def test_summary_cannot_hide_missing_test_executions(self):
        self.check_report('<assemblies><assembly total="2" passed="2" failed="0"><test name="fixture" result="Pass"/></assembly></assemblies>', 'ToolFailure')

    def test_test_failure_with_success_exit_is_tool_failure(self):
        self.check_report('<assemblies><assembly total="1" passed="0" failed="1"><test name="fixture" result="Fail"/></assembly></assemblies>', 'ToolFailure')

    def test_passing_summary_cannot_hide_individual_failure(self):
        self.check_report('<assemblies><assembly total="1" passed="1" failed="0"><test name="fixture" result="Fail"/></assembly></assemblies>', 'ToolFailure')

    def test_mutant_timeout_retains_timeout_state(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(runner, 'run', return_value=runner.Execution('TimedOut', -9, '')):
            root = Path(directory)
            result = runner.tests(root, {'testDll': 'test.dll', 'minimumTests': 1}, root / 'out', 1, expected_names=['fixture'])
            self.assertEqual('TimedOut', result['state'])

    def test_completed_failure_and_success_are_distinguished(self):
        self.check_report('<assemblies><assembly total="1" passed="1" failed="0"><test name="fixture" result="Pass"/></assembly></assemblies>', 'Passed')
        self.check_report('<assemblies><assembly total="1" passed="0" failed="1"><test name="fixture" result="Fail"/></assembly></assemblies>', 'Failed', 2)

    def test_changed_selection_is_not_a_kill(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / 'tests.xml'; p.write_text('<assemblies><assembly total="1" passed="0" failed="1"><test name="fixture" result="Fail"/></assembly></assemblies>')
            self.assertEqual('ToolFailure', runner.test_record(p, runner.Execution('Failed', 2, ''), 1, 2)['state'])

    def test_workspace_escape_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError): runner.inside(Path(directory), '../outside.csproj')

    def test_duplicate_json_keys_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / 'packet.json'; p.write_text('{"state":"Failed","state":"Passed"}')
            with self.assertRaises(ValueError): runner.read(p)

    def test_snapshot_changes_when_source_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / 'code.cs').write_text('class Before {}')
            before = runner.snapshot(root, {'inputs': ['code.cs']})
            (root / 'code.cs').write_text('class After {}')
            self.assertNotEqual(before, runner.snapshot(root, {'inputs': ['code.cs']}))

    def test_nested_sample_coverage_binds_to_exact_declared_source(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); name = 'samples/70/source/src/Domain/Decision.cs'
            source = root / name; source.parent.mkdir(parents=True); source.write_text('if (choice) return result;')
            unknown = root / 'src/Unknown.cs'; unknown.parent.mkdir(parents=True); unknown.write_text('class Unknown {}')
            path = root / 'coverage.xml'
            path.write_text(f'<coverage><class filename="{source}"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class></coverage>')
            rows = {r['id']: r for r in coverage_observations(root, {name: 'a', 'src/Unknown.cs': 'b'}, path, [], {})}
            self.assertTrue(rows[name]['coverageKnown']); self.assertEqual(50, rows[name]['lineCoverage'])
            self.assertFalse(rows['src/Unknown.cs']['coverageKnown'])

    def test_scaffolding_never_overwrites(self):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory); (p / 'keep.txt').write_text('user work')
            with self.assertRaises(ValueError): create(p, 'Example')
            self.assertEqual('user work', (p / 'keep.txt').read_text())

    def test_scaffolding_rejects_code_in_application_identity(self):
        with self.assertRaises(ValueError): create('/tmp/unused-assay-test', 'App; shell')

    def check_report(self, xml, expected, exit_code=0):
        with tempfile.TemporaryDirectory() as directory:
            p = Path(directory) / 'tests.xml'; p.write_text(xml)
            self.assertEqual(expected, runner.test_record(p, runner.Execution('Passed' if not exit_code else 'Failed', exit_code, ''), 1)['state'])


if __name__ == '__main__': unittest.main()
