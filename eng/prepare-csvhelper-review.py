#!/usr/bin/env python3
"""Prepare evidence-bound human review material; never adjudicate automatically."""
import argparse
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)
OUTPUT = ROOT / 'evidence/generated/csvhelper/review'


def validate_responses(responses, packet, packet_sha):
    if responses.get('packetSha256') != packet_sha:
        raise ValueError('Responses must identify this exact review packet.')
    if not isinstance(responses.get('reviewer'), str) or not responses['reviewer'].strip():
        raise ValueError('A self-reported reviewer identity is required.')
    rows = responses.get('reviews', [])
    required = {x['id'] for x in packet['items']}
    if len(rows) != len(required) or {x.get('id') for x in rows} != required:
        raise ValueError('Exactly one response per assigned review item is required.')
    for row in rows:
        if row.get('decision') not in ('accept', 'request-change', 'uncertain') or not isinstance(row.get('rationale'), str) or not row['rationale'].strip():
            raise ValueError('Each item needs a registered decision and review rationale.')
    return {'schema': 'csv-human-review-responses/0.1', 'status': 'responses-provided',
            'identityVerification': 'Self-reported; this tool cannot authenticate identity or independence.',
            'reviewer': responses['reviewer'], 'packetSha256': packet_sha, 'reviews': rows,
            'verificationVerdictChanged': False, 'mutationDenominatorChanged': False,
            'authority': 'Review feedback only. No automatic rule waiver, equivalence exclusion, or Pass.'}


def excerpt(path, line):
    lines = path.read_text().splitlines()
    return [{'line': index + 1, 'text': lines[index]} for index in range(max(0, line - 3), min(len(lines), line + 2))]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--responses', type=Path)
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    if args.responses:
        packet = json.loads((OUTPUT / 'packet.json').read_text())
        result = validate_responses(json.loads(args.responses.read_text()), packet, evidence.sha(OUTPUT / 'packet.json'))
        evidence.write(OUTPUT / 'responses-report.json', result)
        print(json.dumps({'status': result['status'], 'verificationVerdictChanged': False}))
        return
    validation_path = OUTPUT.parent / 'validation.json'
    validation = json.loads(validation_path.read_text())
    investigations_path = evidence.OUTPUT / 'investigations.json'
    investigations = json.loads(investigations_path.read_text())
    if validation['verdict'] != 'pass' or not validation['authoritative'] or investigations['executionStatus'] != 'completed':
        raise ValueError('A completed authoritative sample gate is required.')
    evidence.assert_bindings(investigations['inputSha256'])
    sample = evidence.SAMPLE
    corpus = json.loads((sample / 'mutation-cases.json').read_text())
    expected = json.loads((sample / 'expected-evidence.json').read_text())
    items = []
    for case in corpus['cases']:
        path = sample / case['path']
        observed = next(x for x in investigations['results'] if x['id'] == case['id'])
        items.append({'id': 'mutation:' + case['id'], 'kind': 'mutation', 'requirement': case['requirement'],
                      'source': case['path'], 'sourceSha256': evidence.sha(path), 'excerpt': excerpt(path, case['line']),
                      'mutation': {'before': case['before'], 'after': case['after']},
                      'observedBefore': observed['phases']['before']['state'], 'observedAfter': observed['phases']['after']['state'],
                      'question': 'Is the declared requirement correct and independently supported? A survivor remains in the denominator.'})
    ledger = json.loads((sample / 'review-cases.json').read_text())
    for finding in expected['findings']:
        location = finding['location']; path = sample / location['path']
        review = next(x for x in ledger['advisories'] if x['fingerprint'] == finding['fingerprint'])
        items.append({'id': 'advisory:' + finding['fingerprint'], 'kind': 'advisory', 'finding': finding,
                      'sourceSha256': evidence.sha(path), 'excerpt': excerpt(path, location['startLine']),
                      'agentRationale': review['rationale'], 'question': 'Does this finding warrant a source change or retention with rationale?'})
    packet = {'schema': 'csv-human-review-packet/0.1', 'humanAdjudication': 'pending',
              'authority': 'Agent-prepared handoff; all supplied judgments remain visible and require independent review.',
              'validationSha256': evidence.sha(validation_path), 'investigationsSha256': evidence.sha(investigations_path),
              'inputSha256': investigations['inputSha256'], 'items': items, 'survivor': ledger['survivor'],
              'frameworkDecision': ledger['frameworkDecision']}
    evidence.write(OUTPUT / 'packet.json', packet)
    template = {'packetSha256': evidence.sha(OUTPUT / 'packet.json'), 'reviewer': '',
                'reviews': [{'id': item['id'], 'decision': '', 'rationale': ''} for item in items]}
    evidence.write(OUTPUT / 'response-template.json', template)
    evidence.write(OUTPUT / 'usefulness-protocol.json', {
        'schema': 'csv-advice-study-protocol/0.1', 'status': 'not-executed',
        'authority': 'Protocol only; no participants, timings, or usefulness outcomes have been collected.',
        'primaryOutcome': 'Seconds to a reviewer-proposed investigation that an independent domain reviewer rates useful.',
        'secondaryOutcomes': ['Useful investigations per case', 'Unsupported suggestions', 'Reviewer explanation clarity'],
        'design': 'Counterbalance deterministic-only and provider-assisted advice across unseen CSV cases and reviewers; hide provider names. Register held-out cases before collecting advice. Do not train or tune on those cases.',
        'prerequisites': ['Independent case adjudication', 'Consenting reviewers', 'Configured providers if that arm is used', 'Preregister participant count, assignment and analysis before collection'],
        'reporting': 'Publish observed failures and unavailable arms alongside timings; agent-label agreement is separate. Keep authoritative verification unchanged.'})
    print(json.dumps({'status': 'packet-prepared', 'items': len(items), 'humanAdjudication': 'pending', 'usefulnessStudy': 'not-executed'}))


if __name__ == '__main__':
    main()
