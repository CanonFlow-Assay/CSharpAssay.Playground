#!/usr/bin/env python3
"""Generate hefty physical mocks and compare original CsvHelper with the verified adapter."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/60-csvhelper-functional'
OUTPUT = ROOT / 'evidence/generated/csvhelper/hefty'
PLAN = SAMPLE / 'workloads/hefty-cases.json'
DLL = SAMPLE / 'workloads/CsvFunctional.Workloads/bin/Release/net11.0/CsvFunctional.Workloads.dll'
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


def file_sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest().upper()


def validate_manifest(manifest, plan, directory):
    if manifest.get('Schema') != 'csv-hefty-fixtures/0.1':
        raise ValueError('Unsupported fixture manifest.')
    fixtures = manifest.get('Fixtures', [])
    requested = {x['id']: x for x in plan['cases']}
    if len(fixtures) != len(requested) or {f['Specification']['Id'] for f in fixtures} != set(requested):
        raise ValueError('Missing/duplicate/unrequested heavyweight fixtures.')
    total = 0
    for fixture in fixtures:
        declared = fixture['Specification']
        case = requested[declared['Id']]
        expected = dict(Id=case['id'], Rows=case['rows'], NoteCharacters=case['noteCharacters'],
                        Unicode=case['unicode'], Culture=case['culture'], InvalidEvery=case['invalidEvery'])
        if declared != expected or fixture['FileName'] != case['id'] + '.csv':
            raise ValueError('Fixture differs from the preregistered physical workload.')
        path = directory / fixture['FileName']
        if (not path.is_file() or path.stat().st_size != fixture['FileBytes'] or fixture['FileBytes'] <= 0 or
                file_sha(path) != fixture['FileSha256']):
            raise ValueError('Fixture bytes/hash do not match the manifest.')
        count = case['rows']; interval = case['invalidEvery']
        rejected = count // interval if interval else 0
        id_sum = count * (count + 1) // 2 - (interval * rejected * (rejected + 1) // 2 if interval else 0)
        totals = fixture['Expected']['Totals']
        if (totals['Accepted'] != count - rejected or totals['Rejected'] != rejected or totals['IssueCount'] != 3 * rejected or
                totals['IdSum'] != id_sum or totals['AmountSum'] != id_sum / 100 or totals['NoteCharacters'] != (count - rejected) * case['noteCharacters'] or
                totals['ActiveCount'] != count // 2 - (rejected // 2 if interval % 2 else rejected) or totals['Stop'] != 0 or totals['StopRecord'] != 0):
            raise ValueError('Full fixture expectations violate independent aggregate formulas.')
        for key in ('Expected', 'OriginalExpected'):
            checksum = fixture[key]['OutputSha256']
            if len(checksum) != 64 or any(c not in '0123456789ABCDEF' for c in checksum):
                raise ValueError('Expected complete SHA-256 content evidence.')
        if not interval and fixture['OriginalExpected'] != fixture['Expected']:
            raise ValueError('Valid original reference differs from complete reference.')
        if interval:
            prefix = fixture['OriginalExpected']; t = prefix['Totals']; n = interval - 1
            if prefix['Failure'] != 1 or t['Accepted'] != n or t['Rejected'] != 0 or t['IdSum'] != n * (n + 1) // 2 or t['Stop'] != 1 or t['StopRecord'] != interval:
                raise ValueError('Fail-fast prefix reference is inconsistent.')
        total += fixture['FileBytes']
    if total > plan['maximumTotalFileBytes']:
        raise ValueError('Fixture volume exceeds the explicit disk budget.')
    return fixtures


def validate_child(raw, fixture, pipeline, mode):
    if (raw.get('schema') != 'csv-hefty-child/0.1' or raw.get('executionStatus') != 'completed' or
            raw.get('caseId') != fixture['Specification']['Id'] or raw.get('pipeline') != pipeline):
        raise ValueError('Unexpected/incomplete child execution.')
    settings = ('1', '1') if mode == 'tiered-pgo' else ('0', '0')
    if (raw.get('tieredCompilation'), raw.get('tieredPgo')) != settings or raw.get('buildConfiguration') != 'Release':
        raise ValueError('Runtime mode or optimized build context differs.')
    if not raw.get('runtime') or not raw.get('os') or type(raw.get('peakWorkingSetBytes')) is not int or raw['peakWorkingSetBytes'] <= 0:
        raise ValueError('Missing process/runtime memory context.')
    rows = raw.get('samples', [])
    if len(rows) != 5 or {r['iteration'] for r in rows} != set(range(-2, 3)):
        raise ValueError('Expected exactly two warmups and three distinct measured runs.')
    expected = fixture['OriginalExpected'] if pipeline == 'Original' else fixture['Expected']
    for row in rows:
        if (type(row['iteration']) is not int or type(row['warmup']) is not bool or row['warmup'] != (row['iteration'] < 0) or
                type(row['elapsedMilliseconds']) not in (int, float) or not math.isfinite(row['elapsedMilliseconds']) or row['elapsedMilliseconds'] <= 0 or
                type(row['allocatedBytes']) is not int or row['allocatedBytes'] <= 0 or any(type(row[g]) is not int or row[g] < 0 for g in ('gen0', 'gen1', 'gen2'))):
            raise ValueError('Invalid measurements, GC counts or warmup identity.')
        if row['result'] != expected or row['equivalentToCompleteExpected'] != (expected == fixture['Expected']):
            raise ValueError('Output or equivalence claim differs from the complete/prefix reference.')
    return [r for r in rows if not r['warmup']]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reuse-fixtures', action='store_true')
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'csv-hefty-comparison/0.1', 'executionStatus': 'running', 'results': [], 'comparisons': [],
              'performanceGateApplied': False, 'providerRequests': 0,
              'authority': 'Synthetic local warm-cache file benchmark; original package versus Assay-verified consumer, not a whole-library functional fork.'}
    evidence.write(OUTPUT / 'comparison.json', report)
    try:
        plan = json.loads(PLAN.read_text())
        if plan['schema'] != 'csv-hefty-plan/0.1' or plan['runtimeModes'] != ['tiered-pgo', 'non-tiered'] or plan['warmupsPerChild'] != 2 or plan['measuredRunsPerChild'] != 3 or not 1 <= len(plan['cases']) <= 4:
            raise ValueError('Unsupported/beyond-budget heavyweight plan.')
        if sum(c['rows'] * (6 * c['noteCharacters'] + 96) for c in plan['cases']) > plan['maximumTotalFileBytes']:
            raise ValueError('Conservative pre-generation byte estimate exceeds the plan budget.')
        validation = json.loads((OUTPUT.parent / 'validation.json').read_text())
        if validation['executionStatus'] != 'completed' or validation['verdict'] != 'pass' or not validation['authoritative']:
            raise ValueError('Run ./eng/run-csvhelper.sh first for a complete authoritative sample receipt.')
        investigations = json.loads((OUTPUT.parent / 'functional/investigations.json').read_text())
        evidence.assert_bindings(investigations['inputSha256'])
        if evidence.input_bindings() != investigations['inputSha256']:
            raise ValueError('Sample differs from the source-bound verified investigation run.')
        report['assayValidationSha256'] = evidence.sha(OUTPUT.parent / 'validation.json')
        report['assayReceiptSha256'] = evidence.sha(OUTPUT.parent / 'verify.json')
        bindings = evidence.input_bindings()
        bindings[str(Path(__file__).relative_to(ROOT))] = evidence.sha(Path(__file__))
        report['inputSha256'] = bindings
        report['artifactSha256'] = evidence.artifacts(SAMPLE)
        report['csvHelperPackage'] = json.loads((SAMPLE / 'upstream.json').read_text())['package']
        report['csvHelperAssemblySha256'] = file_sha(DLL.with_name('CsvHelper.dll'))
        data = OUTPUT / 'data'; data.mkdir(exist_ok=True)
        if not args.reuse_fixtures or not (data / 'manifest.json').is_file():
            generated = subprocess.run(['dotnet', str(DLL), '--hefty-generate', str(data)], cwd=ROOT, text=True, capture_output=True, timeout=300)
            (OUTPUT / 'generation.log').write_text(generated.stdout + generated.stderr)
            evidence.require_success(generated, 'Physical fixture generation')
        manifest = json.loads((data / 'manifest.json').read_text())
        fixtures = validate_manifest(manifest, plan, data)
        report['manifestSha256'] = evidence.sha(data / 'manifest.json')
        report['fixtures'] = fixtures
        report['totalFileBytes'] = sum(f['FileBytes'] for f in fixtures)
        evidence.write(OUTPUT / 'comparison.json', report)
        for mode_index, mode in enumerate(plan['runtimeModes']):
            environment = dict(os.environ, DOTNET_TieredCompilation='1' if mode == 'tiered-pgo' else '0', DOTNET_TieredPGO='1' if mode == 'tiered-pgo' else '0')
            for index, fixture in enumerate(fixtures):
                identifier = fixture['Specification']['Id']
                pipelines = ['Original', 'Assayed']
                if fixture['Specification']['InvalidEvery']:
                    pipelines.insert(1, 'ResilientOriginal')
                if (mode_index + index) % 2:
                    pipelines.reverse()
                for pipeline in pipelines:
                    report['current'] = {'mode': mode, 'case': identifier, 'pipeline': pipeline}
                    evidence.write(OUTPUT / 'comparison.json', report)
                    child = subprocess.run(['dotnet', str(DLL), '--hefty-run', str(data / 'manifest.json'), identifier, pipeline], cwd=ROOT, env=environment, text=True, capture_output=True, timeout=plan['maximumChildSeconds'])
                    path = OUTPUT / 'raw' / mode / (identifier + '-' + pipeline + '.json')
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.with_suffix('.log').write_text(child.stdout + child.stderr)
                    evidence.require_success(child, 'Heavyweight pipeline ' + identifier + '/' + pipeline)
                    raw = json.loads(child.stdout)
                    selected = validate_child(raw, fixture, pipeline, mode)
                    evidence.write(path, raw)
                    times = [r['elapsedMilliseconds'] for r in selected]
                    allocations = [r['allocatedBytes'] for r in selected]
                    comparable = selected[0]['equivalentToCompleteExpected']
                    median = statistics.median(times)
                    row = {'mode': mode, 'case': identifier, 'pipeline': pipeline, 'completeOutputEquivalent': comparable,
                           'medianMilliseconds': round(median, 3), 'minMilliseconds': round(min(times), 3), 'maxMilliseconds': round(max(times), 3),
                           'medianRowsPerSecond': round(fixture['Specification']['Rows'] * 1000 / median) if comparable else None,
                           'medianMiBPerSecond': round(fixture['FileBytes'] / 1048576 * 1000 / median, 2) if comparable else None,
                           'medianAllocatedBytes': statistics.median(allocations),
                           'allocatedBytesPerRow': round(statistics.median(allocations) / fixture['Specification']['Rows'], 2) if comparable else None,
                           'processPeakRssMiB': round(raw['peakWorkingSetBytes'] / 1048576, 2),
                           'result': selected[0]['result'], 'context': {k: raw[k] for k in raw if k != 'samples'},
                           'rawReportSha256': evidence.sha(path)}
                    report['results'].append(row)
                    evidence.write(OUTPUT / 'comparison.json', report)
                    print(mode, identifier, pipeline, str(row['medianMilliseconds']) + 'ms', 'complete=' + str(comparable), flush=True)
        for mode in plan['runtimeModes']:
            for fixture in fixtures:
                identifier = fixture['Specification']['Id']
                candidates = [r for r in report['results'] if r['case'] == identifier and r['mode'] == mode and r['completeOutputEquivalent']]
                baseline_name = 'ResilientOriginal' if fixture['Specification']['InvalidEvery'] else 'Original'
                baseline = next(r for r in candidates if r['pipeline'] == baseline_name)
                assayed = next(r for r in candidates if r['pipeline'] == 'Assayed')
                if assayed['result'] != baseline['result']:
                    raise ValueError('Matched comparisons must have identical complete outputs.')
                report['comparisons'].append({'mode': mode, 'case': identifier, 'baseline': baseline_name,
                                              'assayedToOriginalTimeRatio': round(assayed['medianMilliseconds'] / baseline['medianMilliseconds'], 3),
                                              'assayedToOriginalAllocationRatio': round(assayed['medianAllocatedBytes'] / baseline['medianAllocatedBytes'], 3)})
        validate_manifest(json.loads((data / 'manifest.json').read_text()), plan, data)
        if evidence.sha(data / 'manifest.json') != report['manifestSha256']:
            raise ValueError('Fixture manifest changed during measurement.')
        evidence.assert_bindings(bindings)
        current = evidence.input_bindings()
        current[str(Path(__file__).relative_to(ROOT))] = evidence.sha(Path(__file__))
        if current != bindings or file_sha(DLL.with_name('CsvHelper.dll')) != report['csvHelperAssemblySha256']:
            raise ValueError('Input inventory or original CsvHelper binary changed during measurement.')
        if evidence.artifacts(SAMPLE) != report['artifactSha256']:
            raise ValueError('Compiled artifacts changed during measurement.')
        report['executionStatus'] = 'completed'
        report.pop('current', None)
    except (OSError, ValueError, RuntimeError, KeyError, StopIteration, subprocess.SubprocessError) as failure:
        report.update(executionStatus='failed', failure=str(failure))
        evidence.write(OUTPUT / 'comparison.json', report)
        raise
    evidence.write(OUTPUT / 'comparison.json', report)
    print(json.dumps({'executionStatus': report['executionStatus'], 'totalFileBytes': report['totalFileBytes'], 'comparisons': report['comparisons']}, indent=2))


if __name__ == '__main__':
    main()
