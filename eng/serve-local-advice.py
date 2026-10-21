#!/usr/bin/env python3
"""Optional loopback service: SystemOne choice gateway or trained SetFit labels.
No dependencies for Ollama. SetFit is imported only for --backend setfit.
"""
import argparse
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
from pathlib import Path
import urllib.request

ACTIONS = {'inspect_assertion', 'inspect_reachability', 'review_contract', 'human_review'}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def ollama_choice(model, state, question, endpoint):
    criteria = question['criteria']
    if question.get('type') != 'choice' or not isinstance(criteria, dict) or not 2 <= len(criteria) <= 32:
        raise ValueError('Only bounded choice questions are supported.')
    if not all(isinstance(k, str) and isinstance(v, str) and k and v for k, v in criteria.items()):
        raise ValueError('Choices must have string values and descriptions.')
    instructions = question['instructions']
    if not isinstance(instructions, str):
        raise ValueError('Instructions must be text.')
    schema = {'type': 'object', 'properties': {'choice': {'type': 'string', 'enum': list(criteria)}},
              'required': ['choice'], 'additionalProperties': False}
    payload = {'model': model, 'stream': False, 'think': False, 'keep_alive': '0s',
               'options': {'temperature': 0, 'num_predict': 128, 'num_ctx': 2048, 'num_thread': 4},
               'messages': [{'role': 'system', 'content': instructions + '\nChoose using: ' + json.dumps(criteria)},
                            {'role': 'user', 'content': json.dumps(state)}], 'format': schema}
    request = urllib.request.Request(endpoint, json.dumps(payload).encode(), {'Content-Type': 'application/json'})
    with urllib.request.build_opener(NoRedirect).open(request, timeout=30) as response:
        raw = response.read(65537)
    if len(raw) > 65536:
        raise ValueError('Oversized upstream response.')
    result = json.loads(raw)
    if result.get('done') is not True or result.get('done_reason') != 'stop':
        raise ValueError('Incomplete generation.')
    choice = json.loads(result['message']['content'])['choice']
    if choice not in criteria:
        return {'type': 'refusal'}
    # A generated label supplies neither a measured probability nor confidence.
    return {'type': 'choice', 'choice': choice}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', choices=['ollama', 'setfit'], required=True)
    parser.add_argument('--model', required=True, help='Ollama model ID, or an existing local trained SetFit model directory')
    parser.add_argument('--port', type=int, default=8001)
    parser.add_argument('--ollama-port', type=int, default=11434)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535 or not 1 <= args.ollama_port <= 65535:
        parser.error('Ports must be in 1..65535.')
    classifier = None
    if args.backend == 'setfit':
        model_path = Path(args.model).expanduser().resolve()
        if not model_path.is_dir():
            parser.error('SetFit requires an existing reviewed local model; no model is downloaded.')
        from setfit import SetFitModel
        classifier = SetFitModel.from_pretrained(str(model_path), local_files_only=True)
        if set(classifier.labels or []) != ACTIONS:
            parser.error('SetFit labels must match the four registered Assay actions.')

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass  # Request content and secrets never enter access logs.

        def send_json(self, status, result):
            data = json.dumps(result).encode()
            self.send_response(status)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_POST(self):
            try:
                if self.headers.get('Transfer-Encoding'):
                    raise ValueError('Chunked input is unsupported.')
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 < length <= 65536:
                    raise ValueError('Invalid input size.')
                data = json.loads(self.rfile.read(length))
                if args.backend == 'setfit' and self.path == '/predict':
                    if data.get('model') != args.model or not isinstance(data.get('text'), str):
                        raise ValueError('Model or text mismatch.')
                    label = str(classifier.predict([data['text']])[0])
                    self.send_json(200, {'label': label if label in ACTIONS else 'human_review'})
                    return
                if args.backend == 'ollama' and self.path == '/v1/systemone':
                    if data.get('model') != args.model:
                        raise ValueError('Model mismatch.')
                    questions = data['questions']
                    if not isinstance(questions, dict) or len(questions) != 1:
                        raise ValueError('This bounded gateway supports one choice question per request.')
                    answers = {name: ollama_choice(args.model, data['state'], question,
                               f'http://127.0.0.1:{args.ollama_port}/api/chat') for name, question in questions.items()}
                    self.send_json(200, {'model': args.model, 'answers': answers,
                                        'adapter': 'assay-generative-choice-gateway/0.1'})
                    return
                self.send_json(404, {'error': 'Unsupported backend endpoint.'})
            except (ValueError, KeyError, TypeError, RecursionError):
                self.send_json(422, {'error': 'Invalid request or upstream output.'})
            except Exception:
                self.send_json(503, {'error': 'Local advisor unavailable; no retry performed.'})

        def setup(self):
            super().setup()
            self.connection.settimeout(35)

    print(f'Local {args.backend} adapter listening on 127.0.0.1:{args.port}', flush=True)
    HTTPServer(('127.0.0.1', args.port), Handler).serve_forever()


if __name__ == '__main__':
    main()
