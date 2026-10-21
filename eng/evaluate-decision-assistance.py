#!/usr/bin/env python3
"""Evaluate a frozen synthetic scenario set. Offline by default; live calls require --live."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
SCENARIOS = ROOT / 'samples/50-decision-assistance/corpus/scenarios.json'
OUTPUT = ROOT / 'evidence/generated/decision-assistance'
CLI = ROOT / 'samples/50-decision-assistance/src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'
PROVIDERS = ('none', 'openai', 'jev', 'clef', 'clef-flash', 'clef-local', 'laya', 'qwen', 'chat', 'setfit', 'custom')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def invoke(path, provider):
    start = time.monotonic()
    try:
        result = subprocess.run(['dotnet', str(CLI), str(path), '--provider=' + provider],
                                cwd=ROOT, capture_output=True, text=True, timeout=40)
        if result.returncode:
            return {'executionStatus': 'cli_failed', 'exitCode': result.returncode}
        advice = json.loads(result.stdout)
        if advice['schema'] != 'playground-decision-advice/0.1':
            raise ValueError('Unexpected advice schema.')
        return {'executionStatus': 'completed', 'elapsedSeconds': round(time.monotonic() - start, 3), 'advice': advice}
    except (subprocess.TimeoutExpired, OSError, ValueError, KeyError, TypeError):
        return {'executionStatus': 'invalid_or_unavailable_cli'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--provider', action='append', choices=PROVIDERS, help='Repeat for an explicitly requested comparison; defaults to none.')
    parser.add_argument('--live', action='store_true', help='Authorize configured provider calls for this run.')
    parser.add_argument('--max-requests', type=int, default=8, help='Upper bound on eligible provider calls, checked before any live call (1..32).')
    args = parser.parse_args()
    providers = args.provider or ['none']
    if len(providers) != len(set(providers)) or len(providers) > 3:
        parser.error('Select at most three distinct providers.')
    live = any(p != 'none' for p in providers)
    if live and not args.live:
        parser.error('Provider calls require explicit --live; no calls were made.')
    if not 1 <= args.max_requests <= 32:
        parser.error('--max-requests must be in 1..32.')
    for provider in providers:
        variable = {'openai': 'OPENAI_API_KEY', 'jev': 'TYPESAFE_API_KEY', 'clef': 'CLOUDFLARE_AUTH_TOKEN', 'clef-flash': 'CLOUDFLARE_AUTH_TOKEN'}.get(provider)
        if variable and not os.environ.get(variable):
            parser.error(variable + ' must be configured; no calls were made.')
    if not CLI.is_file() or not (OUTPUT / 'experiment.json').is_file():
        parser.error('Build the CLI and run eng/run-decision-assistance.py first, or use eng/try-decision-assistance.sh.')
    corpus = json.loads(SCENARIOS.read_text())
    if corpus.get('schema') != 'playground-advice-scenarios/0.1' or corpus.get('synthetic') is not True:
        parser.error('Unsupported scenario set.')
    base = json.loads((OUTPUT / 'experiment.json').read_text())
    report = {'schema': 'playground-advice-evaluation/0.1', 'synthetic': True, 'hypotheticalStates': True,
              'labelReviewStatus': corpus['labelReviewStatus'], 'qualityGateApplied': False,
              'scenarioSetSha256': digest(SCENARIOS.read_bytes()), 'cliSha256': digest(CLI.read_bytes()),
              'sourceSha256': base['sourceSha256'], 'providers': providers, 'maxProviderRequests': args.max_requests,
              'providerRequestsEligible': 0, 'results': [],
              'authority': 'Scenario evaluation only; neither model qualification nor an Assay verification receipt.'}
    source_files = ['eng/evaluate-decision-assistance.py',
                    'samples/50-decision-assistance/src/DecisionAssist.Domain/Assistance.cs',
                    'samples/50-decision-assistance/src/DecisionAssist.Cli/ProviderAdvice.cs',
                    'samples/50-decision-assistance/src/DecisionAssist.Cli/JevAdvice.cs',
                    'samples/50-decision-assistance/src/DecisionAssist.Cli/Program.cs']
    report['implementationSha256'] = {name: digest((ROOT / name).read_bytes()) for name in source_files}
    stopped = False
    baseline_ok = True
    with tempfile.TemporaryDirectory(prefix='assay-advice-scenarios-') as temporary:
        prepared = []
        for case in corpus['cases']:
            path = Path(temporary) / (case['id'] + '.json')
            scenario = {'schema': 'playground-mutation-experiment/0.1', 'synthetic': True,
                        'sourceSha256': base['sourceSha256'], 'hypotheticalState': True,
                        'scenarioId': case['id'], **case['observations']}
            path.write_text(json.dumps(scenario))
            baseline = invoke(path, 'none')
            if baseline['executionStatus'] != 'completed':
                parser.error('Offline baseline failed; no provider calls were made.')
            baseline_advice = baseline['advice']
            match = baseline_advice['action'] == case['expectedAction'] and baseline_advice['adviceStatus'] == case['expectedStatus']
            baseline_ok = baseline_ok and match and baseline_advice['verificationVerdict'] == 'Inconclusive'
            eligible = baseline_advice['adviceStatus'] == 'suggested'
            report['providerRequestsEligible'] += int(eligible) * sum(p != 'none' for p in providers)
            prepared.append((case, path, baseline, match, eligible))
        if not baseline_ok:
            parser.error('Offline policy expectations failed; no provider calls were made.')
        if report['providerRequestsEligible'] > args.max_requests:
            parser.error('Selected providers exceed --max-requests; no provider calls were made.')
        for case, path, baseline, match, eligible in prepared:
            row = {'id': case['id'], 'scenarioInputSha256': digest(path.read_bytes()), 'expectedAction': case['expectedAction'],
                   'expectedStatus': case['expectedStatus'], 'reason': case['reason'], 'baselineMatches': match,
                   'baseline': baseline, 'providerEligible': eligible, 'observations': []}
            for provider in providers:
                if stopped:
                    row['observations'].append({'provider': provider, 'executionStatus': 'not_run_after_failure'})
                    continue
                result = baseline if provider == 'none' else invoke(path, provider)
                observation = {'provider': provider, **result}
                if result['executionStatus'] == 'completed':
                    advice = result['advice']
                    observation['matchesDraftExpectation'] = advice['action'] == case['expectedAction'] and advice['adviceStatus'] == case['expectedStatus']
                    observation['verdictUnchanged'] = advice['verificationVerdict'] == 'Inconclusive'
                    if not observation['verdictUnchanged'] or advice['adviceStatus'] == 'unavailable':
                        stopped = True
                else:
                    stopped = True
                row['observations'].append(observation)
            report['results'].append(row)
    report['executionStatus'] = 'stopped_after_failure' if stopped else 'completed'
    report['summary'] = {provider: {
        'completed': sum(o['executionStatus'] == 'completed' for row in report['results'] for o in row['observations'] if o['provider'] == provider),
        'eligibleCasesMatchingDraftExpectation': sum(row['providerEligible'] and o.get('matchesDraftExpectation', False) for row in report['results'] for o in row['observations'] if o['provider'] == provider),
        'eligibleCases': sum(row['providerEligible'] for row in report['results']),
        'localReviewCases': sum(not row['providerEligible'] for row in report['results'])} for provider in providers}
    output = OUTPUT / ('evaluation-' + '-'.join(providers) + '.json')
    output.write_text(json.dumps(report, sort_keys=True, indent=2) + '\n')
    print(json.dumps({'executionStatus': report['executionStatus'], 'summary': report['summary'], 'report': str(output.relative_to(ROOT)), 'qualityGateApplied': False}))
    return 2 if stopped else 0


if __name__ == '__main__':
    raise SystemExit(main())
