#!/usr/bin/env python3
"""Run a bounded, source-bound local CSV throughput/allocation comparison."""
import importlib.util
import json
import math
import os
from pathlib import Path
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/60-csvhelper-functional'
OUTPUT = ROOT / 'evidence/generated/csvhelper/streaming'
PROJECT = SAMPLE / 'workloads/CsvFunctional.Workloads/CsvFunctional.Workloads.csproj'
DLL = PROJECT.parent / 'bin/Release/net11.0/CsvFunctional.Workloads.dll'
spec = importlib.util.spec_from_file_location('csv_evidence', ROOT / 'eng/run-csvhelper-functional.py')
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


def validate(raw):
    if raw.get('schema') != 'csv-stream-benchmark/0.1' or raw.get('executionStatus') != 'completed':
        raise ValueError('Incomplete benchmark.')
    rows = raw.get('samples', [])
    if len(rows) != 28:
        raise ValueError('Expected both pipelines, two sizes, two warmups and five measured runs.')
    identities = {(r['mode'], r['rows'], r['iteration']) for r in rows}
    expected = {(mode, count, iteration) for mode in ('direct', 'functional') for count in (10000, 100000) for iteration in range(-2, 5)}
    if identities != expected or len(identities) != len(rows):
        raise ValueError('Missing/duplicate benchmark samples.')
    digests = {}
    for row in rows:
        if type(row['iteration']) is not int or type(row['rows']) is not int or type(row['warmup']) is not bool:
            raise ValueError('Invalid sample identity types.')
        duration = row['elapsedMilliseconds']
        allocated = row['allocatedBytes']
        if (type(duration) not in (int, float) or not math.isfinite(duration) or duration <= 0 or
                type(allocated) is not int or allocated < 0 or row['warmup'] != (row['iteration'] < 0)):
            raise ValueError('Invalid measurement or warmup classification.')
        count = row['rows']
        digest = row['digest']
        if set(digest) != {'Accepted', 'Rejected', 'IdSum', 'AmountSum', 'ActiveCount', 'NoteCharacters', 'Fingerprint', 'IssueCount', 'Stop', 'StopRecord'} or type(digest['Fingerprint']) is not int or not 0 <= digest['Fingerprint'] <= 2 ** 64 - 1 or digest['NoteCharacters'] <= 0:
            raise ValueError('Incomplete content digest.')
        if (digest['Accepted'] != count or digest['Rejected'] != 0 or digest['IssueCount'] != 0 or digest['Stop'] != 0 or digest['StopRecord'] != 0 or
                digest['IdSum'] != count * (count + 1) // 2 or digest['AmountSum'] != count * (count + 1) // 2 / 100 or digest['ActiveCount'] != count // 2):
            raise ValueError('Workload output does not meet closed-form expectations.')
        if count in digests and digests[count] != digest:
            raise ValueError('Pipelines/iterations produced unequal output digests.')
        digests[count] = digest
    if raw.get('tieredCompilation') != '0' or raw.get('buildConfiguration') != 'Release':
        raise ValueError('Controlled optimized Release measurement context is missing.')
    if not raw.get('runtime') or not raw.get('os') or not raw.get('scope'):
        raise ValueError('Runtime/measurement context missing.')
    return rows


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'csv-streaming-validation/0.1', 'executionStatus': 'running', 'performanceGateApplied': False,
              'authority': 'Local diagnostic benchmark; no cross-host, production or superiority qualification.'}
    evidence.write(OUTPUT / 'benchmark.json', report)
    try:
        for operation, argv in (
            ('restore', ['dotnet', 'restore', PROJECT, '--locked-mode']),
            ('build', ['dotnet', 'build', PROJECT, '--no-restore', '-c', 'Release'])):
            evidence.require_success(evidence.run(argv, ROOT, OUTPUT / (operation + '.log')), 'Workload ' + operation)
        bindings = evidence.input_bindings()
        bindings[str(Path(__file__).relative_to(ROOT))] = evidence.sha(Path(__file__))
        report['inputSha256'] = bindings
        report['artifactSha256'] = evidence.artifacts(SAMPLE)
        environment = dict(os.environ, DOTNET_TieredCompilation='0')
        result = subprocess.run(['dotnet', str(DLL), '--benchmark'], cwd=ROOT, env=environment, text=True, capture_output=True, timeout=180)
        (OUTPUT / 'benchmark-execution.log').write_text(result.stdout + result.stderr + '\nprocessExitCode=' + str(result.returncode) + '\n')
        report['measurementSettings'] = {'DOTNET_TieredCompilation': '0', 'rationale': 'Disable tier transitions between short repeated runs; results describe this controlled setting.'}
        evidence.require_success(result, 'Benchmark correctness/execution')
        raw = json.loads(result.stdout)
        rows = validate(raw)
        evidence.write(OUTPUT / 'benchmark-raw.json', raw)
        summaries = []
        for count in (10000, 100000):
            modes = {}
            for mode in ('direct', 'functional'):
                selected = [r for r in rows if r['rows'] == count and r['mode'] == mode and not r['warmup']]
                times = [r['elapsedMilliseconds'] for r in selected]
                allocations = [r['allocatedBytes'] for r in selected]
                median = statistics.median(times)
                modes[mode] = {'measuredRuns': len(selected), 'medianMilliseconds': round(median, 3),
                               'minMilliseconds': round(min(times), 3), 'maxMilliseconds': round(max(times), 3),
                               'medianRowsPerSecond': round(count * 1000 / median),
                               'medianAllocatedBytes': statistics.median(allocations),
                               'medianAllocatedBytesPerRow': round(statistics.median(allocations) / count, 2)}
            summaries.append({'rows': count, 'pipelines': modes,
                              'functionalToDirectTimeRatio': round(modes['functional']['medianMilliseconds'] / modes['direct']['medianMilliseconds'], 3),
                              'functionalToDirectAllocationRatio': round(modes['functional']['medianAllocatedBytes'] / modes['direct']['medianAllocatedBytes'], 3)})
        evidence.assert_bindings(bindings)
        current = evidence.input_bindings()
        current[str(Path(__file__).relative_to(ROOT))] = evidence.sha(Path(__file__))
        if current != bindings:
            raise ValueError('Input inventory changed during measurement.')
        if evidence.artifacts(SAMPLE) != report['artifactSha256']:
            raise ValueError('Compiled artifacts changed during measurement.')
        report.update(executionStatus='completed', summaries=summaries, context={k: raw[k] for k in raw if k != 'samples'},
                      rawReportSha256=evidence.sha(OUTPUT / 'benchmark-raw.json'),
                      requirementsSha256=evidence.sha(SAMPLE / 'workloads/requirements.json'), providerRequests=0)
    except (OSError, ValueError, RuntimeError, KeyError, subprocess.SubprocessError) as failure:
        report.update(executionStatus='failed', failure=str(failure))
        evidence.write(OUTPUT / 'benchmark.json', report)
        raise
    evidence.write(OUTPUT / 'benchmark.json', report)
    print(json.dumps({'executionStatus': report['executionStatus'], 'summaries': report['summaries']}, indent=2))


if __name__ == '__main__':
    main()
