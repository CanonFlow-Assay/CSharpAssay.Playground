#!/usr/bin/env python3
"""Compare frozen before/after measurements without changing verification authority."""
import importlib.util
import json
from pathlib import Path
import statistics

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('csv_hefty', ROOT / 'eng/run-csvhelper-hefty.py')
hefty = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hefty)
OUTPUT = hefty.OUTPUT.parent / 'milestones'


def main():
    hefty.evidence.write(OUTPUT / 'status.json', {'schema': 'csv-milestone-status/0.1', 'executionStatus': 'running'})
    archived = OUTPUT / 'before'
    before_path = archived / 'evidence/hefty/comparison.json'
    after_path = hefty.OUTPUT / 'comparison.json'
    before = json.loads(before_path.read_text())
    after = json.loads(after_path.read_text())
    if any(x['executionStatus'] != 'completed' or len(x['results']) != 18 or len(x['comparisons']) != 8 for x in (before, after)):
        raise ValueError('Two complete heavyweight experiments are required.')
    if before['fixtures'] != after['fixtures'] or before['csvHelperAssemblySha256'] != after['csvHelperAssemblySha256']:
        raise ValueError('Frozen fixture contents or original package binary changed.')
    for name, checksum in before['inputSha256'].items():
        if hefty.evidence.sha(archived / 'inputs' / name) != checksum:
            raise ValueError('Archived baseline source was altered.')
    if before['assayValidationSha256'] != hefty.evidence.sha(archived / 'evidence/validation.json') or before['assayReceiptSha256'] != hefty.evidence.sha(archived / 'evidence/verify.json'):
        raise ValueError('Archived verification evidence differs from the measured baseline.')
    for row in before['results']:
        path = archived / 'evidence/hefty/raw' / row['mode'] / (row['case'] + '-' + row['pipeline'] + '.json')
        if hefty.evidence.sha(path) != row['rawReportSha256']:
            raise ValueError('Archived raw measurement was altered.')
        fixture = next(f for f in before['fixtures'] if f['Specification']['Id'] == row['case'])
        hefty.validate_child(json.loads(path.read_text()), fixture, row['pipeline'], row['mode'])
    hefty.evidence.assert_bindings(after['inputSha256'])
    if hefty.evidence.artifacts(hefty.SAMPLE) != after['artifactSha256']:
        raise ValueError('Current compiled artifacts differ from measured artifacts.')
    comparisons = []
    for new in after['results']:
        old = next(x for x in before['results'] if (x['case'], x['pipeline'], x['mode']) == (new['case'], new['pipeline'], new['mode']))
        if new['result'] != old['result']:
            raise ValueError('Optimization changed complete/prefix output.')
        for key in ('runtime', 'architecture', 'os', 'serverGc', 'processorCount', 'tieredCompilation', 'tieredPgo', 'buildConfiguration'):
            if new['context'][key] != old['context'][key]:
                raise ValueError('Measurement contexts differ: ' + key)
        comparisons.append({'mode': new['mode'], 'case': new['case'], 'pipeline': new['pipeline'],
                            'completeOutputEquivalent': new['completeOutputEquivalent'],
                            'beforeMilliseconds': old['medianMilliseconds'], 'afterMilliseconds': new['medianMilliseconds'],
                            'afterToBeforeTimeRatio': round(new['medianMilliseconds'] / old['medianMilliseconds'], 4),
                            'beforeAllocatedBytes': old['medianAllocatedBytes'], 'afterAllocatedBytes': new['medianAllocatedBytes'],
                            'afterToBeforeAllocationRatio': round(new['medianAllocatedBytes'] / old['medianAllocatedBytes'], 4)})
    profiles = [json.loads((OUTPUT / phase / 'allocation-profile.json').read_text()) for phase in ('before', 'after')]
    if profiles[0]['probeSha256'] != profiles[1]['probeSha256'] or profiles[0]['runtime'] != profiles[1]['runtime'] or any(p['tieredCompilation'] != '0' for p in profiles):
        raise ValueError('Allocation probe or runtime changed between profiles.')
    stages = []
    for stage in sorted({x['stage'] for x in profiles[0]['samples']}):
        values = [[x['bytesPerOperation'] for x in p['samples'] if x['stage'] == stage] for p in profiles]
        if any(len(v) != 3 for v in values):
            raise ValueError('Missing allocation probe repetitions.')
        stages.append({'stage': stage, 'beforeBytes': statistics.median(values[0]), 'afterBytes': statistics.median(values[1])})
    report = {'schema': 'csv-milestone-comparison/0.1', 'executionStatus': 'completed',
              'scriptSha256': hefty.evidence.sha(Path(__file__)),
              'beforeReportSha256': hefty.evidence.sha(before_path), 'afterReportSha256': hefty.evidence.sha(after_path),
              'comparisons': comparisons, 'allocationStages': stages,
              'authority': 'Local diagnostic with unchanged physical inputs and observable outputs. Original control timings reveal host/run variation; no significance or production superiority claim.',
              'performanceGateApplied': False}
    hefty.evidence.write(OUTPUT / 'comparison.json', report)
    roundtrip_path = hefty.OUTPUT.parent / 'roundtrip/comparison.json'
    packet_path = hefty.OUTPUT.parent / 'review/packet.json'
    provider_path = hefty.OUTPUT.parent / 'advice-comparison.json'
    validation_path = hefty.OUTPUT.parent / 'validation.json'
    roundtrip = json.loads(roundtrip_path.read_text())
    packet = json.loads(packet_path.read_text())
    provider = json.loads(provider_path.read_text())
    validation = json.loads(validation_path.read_text())
    if roundtrip['executionStatus'] != 'completed' or packet['validationSha256'] != hefty.evidence.sha(validation_path) or roundtrip['validationSha256'] != hefty.evidence.sha(validation_path):
        raise ValueError('Round-trip evidence and review packet must match the current completed gate.')
    hefty.evidence.write(OUTPUT / 'status.json', {
        'schema': 'csv-milestone-status/0.1', 'executionStatus': 'completed-local-work',
        'authoritativeSampleVerdict': validation['verdict'],
        'completed': ['Allocation stage measurements', 'Successful-row allocation optimization', 'Frozen physical-fixture before/after comparison', 'Bounded native-union exporter and exact-byte round trips', 'Expanded mutation corpus', 'Evidence-bound human review handoff and response validation', 'Human usefulness study protocol', 'Native net11 versus upstream compatibility scope decision'],
        'externalRemaining': [
            {'milestone': 'OpenAI/Jev live comparison', 'status': provider['executionStatus'], 'requestsAttempted': provider['requestsAttempted']},
            {'milestone': 'Independent human case/advisory adjudication', 'status': packet['humanAdjudication']},
            {'milestone': 'Human reviewer usefulness study', 'status': 'not-executed; protocol prepared'},
            {'milestone': 'Independent held-out consumer qualification', 'status': 'pending; new writing requirements remain agent-authored'},
            {'milestone': 'Broader engine frameworks and original Windows/multi-target qualification', 'status': 'separate work; not qualified by this sample'}],
        'evidenceSha256': {str(p.relative_to(ROOT)): hefty.evidence.sha(p) for p in [OUTPUT / 'comparison.json', after_path, roundtrip_path, packet_path, provider_path, validation_path]},
        'authority': 'Local implementation/evidence completion only; human work and unavailable services are not represented as completed.'})
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, StopIteration) as failure:
        hefty.evidence.write(OUTPUT / 'status.json', {'schema': 'csv-milestone-status/0.1', 'executionStatus': 'failed', 'failure': str(failure)})
        raise
