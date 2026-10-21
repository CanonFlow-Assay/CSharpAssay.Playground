#!/usr/bin/env python3
"""Compare three agent-labelled CSV investigation actions; no default model calls."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'evidence/generated/csvhelper/advice-comparison.json'
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)
CASES = (('whole-min-boundary', 'before', 'InspectAssertion'),
         ('alias-trim', 'before', 'InspectReachability'),
         ('flag-equivalent-pattern', 'after', 'ReviewContract'))
KEYS = {'openai': 'OPENAI_API_KEY', 'jev': 'TYPESAFE_API_KEY'}


def validate_request(live, providers, maximum, available_count):
    if providers and not live:
        raise ValueError('Explicit --live is required for requested providers.')
    if len(providers) != len(set(providers)) or not 1 <= maximum <= 6:
        raise ValueError('Choose distinct providers and a request budget of 1..6.')
    if len(CASES) * available_count > maximum:
        raise ValueError('The complete selected comparison exceeds the budget; no calls made.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--live', action='store_true')
    parser.add_argument('--provider', action='append', choices=('openai', 'jev'))
    parser.add_argument('--max-requests', type=int, default=6)
    args = parser.parse_args()
    providers = args.provider or []
    configured = {p: bool(os.environ.get(KEYS[p])) for p in providers}
    validate_request(args.live, providers, args.max_requests, sum(configured.values()))
    report = {'schema': 'csv-advice-comparison/0.1', 'executionStatus': 'running', 'maxRequests': args.max_requests,
              'requestsAttempted': 0, 'results': [], 'liveRequested': args.live,
              'providers': {p: 'configured' if yes else 'unavailable_missing_key' for p, yes in configured.items()},
              'authority': 'Three agent-labelled cases; action agreement is not human usefulness, latency or provider qualification.',
              'reviewStatus': 'Human adjudication pending; verifier authority remains separate.'}
    evidence.write(OUTPUT, report)
    try:
        investigation = json.loads((evidence.OUTPUT / 'investigations.json').read_text())
        if investigation['executionStatus'] != 'completed':
            raise ValueError('Complete source-bound investigations are required first.')
        evidence.assert_bindings(investigation['inputSha256'])
        report['investigationsSha256'] = evidence.sha(evidence.OUTPUT / 'investigations.json')
        report['scriptSha256'] = evidence.sha(Path(__file__))
        packets = []
        for identifier, phase, expected in CASES:
            path = evidence.OUTPUT / 'mutations' / identifier / phase / 'experiment.json'
            baseline = evidence.advice(path)
            if baseline['action'] != expected or baseline['adviceStatus'] != 'suggested':
                raise ValueError('Selected case is not eligible with the reviewed agent expectation.')
            packets.append((path, expected))
        stopped = False
        for path, expected in packets:
            for provider, ready in configured.items():
                if not ready or stopped:
                    continue
                report['requestsAttempted'] += 1
                evidence.write(OUTPUT, report)
                started = time.monotonic()
                result = subprocess.run(['dotnet', str(evidence.ASSIST), str(path), '--provider=' + provider], cwd=ROOT, text=True, capture_output=True, timeout=40)
                if result.returncode:
                    raise RuntimeError('Provider invocation failed; no fallback/retry is allowed.')
                value = json.loads(result.stdout)
                if value['verificationVerdict'] != 'Inconclusive':
                    raise ValueError('Provider exceeded the advisory authority boundary.')
                report['results'].append({'case': str(path.relative_to(evidence.OUTPUT)), 'packetSha256': evidence.sha(path),
                                          'provider': provider, 'advice': value, 'agentExpectedAction': expected,
                                          'matchesAgentExpectation': value['action'] == expected,
                                          'cliElapsedMilliseconds': round((time.monotonic() - started) * 1000, 2)})
                stopped = value['adviceStatus'] == 'unavailable'
                evidence.write(OUTPUT, report)
        report['executionStatus'] = 'stopped_after_failure' if stopped else ('completed' if any(configured.values()) else ('unavailable_missing_keys' if args.live else 'not_requested'))
        report['selectedCases'] = [{'id': i, 'phase': p, 'agentExpectedAction': a} for i, p, a in CASES]
    except (OSError, ValueError, RuntimeError, KeyError, subprocess.SubprocessError) as failure:
        report.update(executionStatus='failed', failure=str(failure))
        evidence.write(OUTPUT, report)
        raise
    evidence.write(OUTPUT, report)
    print(json.dumps({'executionStatus': report['executionStatus'], 'requestsAttempted': report['requestsAttempted'], 'providers': report['providers']}, indent=2))


if __name__ == '__main__':
    main()
