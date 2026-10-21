#!/usr/bin/env python3
"""Offline tests for the optional local gateway; no models or network required."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('local_advice', Path(__file__).with_name('serve-local-advice.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GatewayTests(unittest.TestCase):
    def question(self):
        return {'type': 'choice', 'instructions': 'Choose an action.',
                'criteria': {'inspect_assertion': 'Inspect assertion', 'human_review': 'Obtain evidence'}}

    def call(self, response, question=None):
        captured = []
        class Opener:
            def open(self, request, timeout):
                captured.append(json.loads(request.data))
                return io.BytesIO(response)
        with patch.object(module.urllib.request, 'build_opener', return_value=Opener()):
            result = module.ollama_choice('small-model', {'synthetic': True}, question or self.question(), 'http://127.0.0.1:11434/api/chat')
        return result, captured

    def test_maps_without_inventing_confidence(self):
        raw = json.dumps({'done': True, 'done_reason': 'stop', 'message': {'content': '{"choice":"inspect_assertion"}'}}).encode()
        result, sent = self.call(raw)
        self.assertEqual(result, {'type': 'choice', 'choice': 'inspect_assertion'})
        self.assertFalse(sent[0]['think'])
        self.assertEqual(sent[0]['format']['properties']['choice']['enum'], ['inspect_assertion', 'human_review'])

    def test_unknown_action_refuses(self):
        raw = json.dumps({'done': True, 'done_reason': 'stop', 'message': {'content': '{"choice":"Pass"}'}}).encode()
        self.assertEqual(self.call(raw)[0], {'type': 'refusal'})

    def test_incomplete_generation_rejected(self):
        with self.assertRaises(ValueError):
            self.call(b'{"done":false}')

    def test_oversized_output_rejected(self):
        with self.assertRaises(ValueError):
            self.call(b' ' * 65537)

    def test_score_is_not_misrepresented_as_supported(self):
        question = self.question(); question['type'] = 'score'
        with self.assertRaises(ValueError):
            self.call(b'{}', question)


class EvaluationAuthorizationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec = importlib.util.spec_from_file_location('decision_evaluation', Path(__file__).with_name('evaluate-decision-assistance.py'))
        cls.evaluation = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.evaluation)

    def test_keys_do_not_enable_live_calls_without_explicit_flag(self):
        with patch('sys.argv', ['evaluate', '--provider', 'jev']), patch.dict(self.evaluation.os.environ, {'TYPESAFE_API_KEY': 'synthetic'}, clear=True), patch.object(self.evaluation, 'invoke') as invoke, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                self.evaluation.main()
            self.assertEqual(error.exception.code, 2)
            invoke.assert_not_called()

    def test_missing_key_stops_before_any_cli_or_provider_call(self):
        with patch('sys.argv', ['evaluate', '--provider', 'openai', '--live']), patch.dict(self.evaluation.os.environ, {}, clear=True), patch.object(self.evaluation, 'invoke') as invoke, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                self.evaluation.main()
            self.assertEqual(error.exception.code, 2)
            invoke.assert_not_called()

    def test_budget_is_checked_before_provider_inference(self):
        corpus = json.loads(self.evaluation.SCENARIOS.read_text())
        cases = {case['id']: case for case in corpus['cases']}

        def offline_only(path, provider):
            self.assertEqual(provider, 'none')
            case = cases[Path(path).stem]
            return {'executionStatus': 'completed', 'advice': {'action': case['expectedAction'], 'adviceStatus': case['expectedStatus'], 'verificationVerdict': 'Inconclusive'}}

        with patch('sys.argv', ['evaluate', '--provider', 'jev', '--live', '--max-requests', '1']), patch.dict(self.evaluation.os.environ, {'TYPESAFE_API_KEY': 'synthetic'}, clear=True), patch.object(Path, 'is_file', return_value=True), patch.object(Path, 'read_bytes', return_value=b'fixture'), patch.object(Path, 'read_text', autospec=True, side_effect=lambda path, *a, **k: json.dumps(corpus) if path == self.evaluation.SCENARIOS else json.dumps({'sourceSha256': 'a' * 64})), patch.object(self.evaluation, 'invoke', side_effect=offline_only) as invoke, contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                self.evaluation.main()
            self.assertEqual(error.exception.code, 2)
            self.assertEqual(invoke.call_count, len(cases))


if __name__ == '__main__':
    unittest.main()
