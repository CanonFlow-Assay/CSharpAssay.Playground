#!/usr/bin/env python3
"""Assert authoritative sample receipts without hiding reviewed advisory findings."""
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/60-csvhelper-functional'
OUTPUT = ROOT / 'evidence/generated/csvhelper'
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)
expected = json.loads((SAMPLE / 'expected-evidence.json').read_text())
receipt = json.loads((OUTPUT / 'verify.json').read_text())
value = receipt['evidence']
assert receipt['verdict'] == 'pass' and receipt['exitCode'] == 0 and value['authoritative'] is True
assert len(value['projects']) == expected['projects'] and all(p['targetFramework'] == 'net11.0' and p['loaded'] for p in value['projects'])
assert not value['missingEvidence'] and not value['failures'] and not value['suppressions']
assert value['findings'] == expected['findings'] and all(f['disposition'] == 'advise' and not f['suppressed'] for f in value['findings'])
assert len(value['tests']) == 1
run = value['tests'][0]
assert run['total'] == run['passed'] == expected['tests'] and run['failed'] == run['skipped'] == 0 and run['outcome'] == 'passed'
report = json.loads((OUTPUT / 'functional/investigations.json').read_text())
assert report['executionStatus'] == 'completed' and report['providerRequests'] == 0
assert len(report['results']) == len(json.loads((SAMPLE / 'mutation-cases.json').read_text())['cases'])
evidence.assert_bindings(report['inputSha256'])
assert evidence.input_bindings() == report['inputSha256']
assert (OUTPUT / 'verify.json').read_bytes() == (OUTPUT / 'verify-repeat.json').read_bytes()
assert (OUTPUT / 'verify.sarif').read_bytes() == (OUTPUT / 'verify-repeat.sarif').read_bytes()
review = json.loads((SAMPLE / 'review-cases.json').read_text())
assert review['humanAdjudication'] == 'pending'
assert review['survivor']['id'] == 'flag-equivalent-pattern' and review['survivor']['exclusionFromMutationDenominator'] is False
assert {x['fingerprint'] for x in review['advisories']} == {x['fingerprint'] for x in value['findings']}
streaming = json.loads((OUTPUT / 'streaming/benchmark.json').read_text())
assert streaming['executionStatus'] == 'completed' and streaming['performanceGateApplied'] is False
assert streaming['requirementsSha256'] == evidence.sha(SAMPLE / 'workloads/requirements.json')
evidence.assert_bindings(streaming['inputSha256'])
assert streaming['artifactSha256'] == evidence.artifacts(SAMPLE)
provider = json.loads((OUTPUT / 'advice-comparison.json').read_text())
assert provider['executionStatus'] in ('not_requested', 'unavailable_missing_keys', 'completed', 'stopped_after_failure')
assert provider['investigationsSha256'] == evidence.sha(OUTPUT / 'functional/investigations.json')
assert provider['scriptSha256'] == evidence.sha(ROOT / 'eng/evaluate-csvhelper-advice.py')
summary = {'schema': 'csv-functional-validation/0.1', 'executionStatus': 'completed',
           'verdict': receipt['verdict'], 'authoritative': True, 'projects': expected['projects'],
           'tests': expected['tests'], 'advisories': len(value['findings']), 'missingEvidence': 0,
           'toolFailures': 0, 'receiptSha256': evidence.sha(OUTPUT / 'verify.json'),
           'sarifSha256': evidence.sha(OUTPUT / 'verify.sarif'), 'repeatJsonAndSarifByteIdentical': True,
           'mutationSummary': report['summary'], 'coverage': {p: {k: v for k, v in c.items() if k != 'lineHits'} for p, c in report['coverage'].items()},
           'investigationReportSha256': evidence.sha(OUTPUT / 'functional/investigations.json'),
           'providerRequests': provider['requestsAttempted'], 'providerEvaluationStatus': provider['executionStatus'],
           'reviewStatus': expected['reviewStatus'], 'reviewLedgerSha256': evidence.sha(SAMPLE / 'review-cases.json'),
           'benchmarkReportSha256': evidence.sha(OUTPUT / 'streaming/benchmark.json'),
           'benchmarkSummaries': streaming['summaries'], 'evidenceBoundaryTests': 36,
           'scope': 'Functional consumer sample; upstream source assessment remains separate.'}
evidence.write(OUTPUT / 'validation.json', summary)
print(json.dumps(summary, indent=2))
