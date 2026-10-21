#!/usr/bin/env python3
"""Opt-in original versus functional physical read/write comparison."""
import importlib.util
import json
import math
import os
from pathlib import Path
import statistics
import subprocess

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('hefty', ROOT / 'eng/run-csvhelper-hefty.py')
hefty = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hefty)
OUTPUT = hefty.OUTPUT.parent / 'roundtrip'


def validate(raw):
    if raw.get('schema') != 'csv-roundtrip-workload/0.1' or raw.get('executionStatus') != 'completed' or (raw.get('tieredCompilation'), raw.get('tieredPgo')) != ('1', '1'):
        raise ValueError('Incomplete round-trip worker or unexpected runtime settings.')
    fixtures = raw['fixtures']
    if len(fixtures) != 2 or {f['Specification']['Id'] for f in fixtures} != {'roundtrip-invariant', 'roundtrip-german'}:
        raise ValueError('Expected both registered round-trip fixtures.')
    if len(raw['samples']) != 16:
        raise ValueError('Missing or additional round-trip runs.')
    for f in fixtures:
        identifier = f['Specification']['Id']
        expected_spec = dict(Id=identifier, Rows=50_000, NoteCharacters=128, Unicode=True, Culture='de-DE' if identifier.endswith('german') else '', InvalidEvery=0)
        if f['Specification'] != expected_spec:
            raise ValueError('Round-trip fixture differs from the registered workload.')
        hashes = set(); sizes = set()
        for pipeline in ('Original', 'Assayed'):
            rows = [r for r in raw['samples'] if r['caseId'] == identifier and r['pipeline'] == pipeline]
            if len(rows) != 4 or {r['iteration'] for r in rows} != {-1, 0, 1, 2}:
                raise ValueError('Missing/duplicate round-trip pipeline iterations.')
            for r in rows:
                if r['digest'] != f['Expected'] or r['warmup'] != (r['iteration'] < 0):
                    raise ValueError('Round-trip output differs from the complete registered values.')
                if type(r['elapsedMilliseconds']) not in (int, float) or not math.isfinite(r['elapsedMilliseconds']) or r['elapsedMilliseconds'] <= 0 or type(r['allocatedBytes']) is not int or r['allocatedBytes'] <= 0:
                    raise ValueError('Invalid round-trip measurement.')
                checksum = r['outputSha256']
                if len(checksum) != 64 or any(c not in '0123456789ABCDEF' for c in checksum) or type(r['outputBytes']) is not int or r['outputBytes'] <= 0:
                    raise ValueError('Missing full output byte/hash evidence.')
                hashes.add(checksum); sizes.add(r['outputBytes'])
        if len(hashes) != 1 or len(sizes) != 1:
            raise ValueError('Original and assayed physical output differs.')
    return fixtures


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'csv-roundtrip-comparison/0.1', 'executionStatus': 'running', 'performanceGateApplied': False}
    hefty.evidence.write(OUTPUT / 'comparison.json', report)
    try:
        validation = json.loads((OUTPUT.parent / 'validation.json').read_text())
        investigations = json.loads((hefty.evidence.OUTPUT / 'investigations.json').read_text())
        if validation['verdict'] != 'pass' or not validation['authoritative'] or investigations['executionStatus'] != 'completed':
            raise ValueError('Run the complete authoritative sample gate first.')
        bindings = hefty.evidence.input_bindings()
        if bindings != investigations['inputSha256']:
            raise ValueError('Source differs from verified sample.')
        bindings[str(Path(__file__).relative_to(ROOT))] = hefty.evidence.sha(Path(__file__))
        artifacts = hefty.evidence.artifacts(hefty.SAMPLE)
        package_hash = hefty.file_sha(hefty.DLL.with_name('CsvHelper.dll'))
        data = OUTPUT / 'data'; data.mkdir(exist_ok=True)
        child = subprocess.run(['dotnet', str(hefty.DLL), '--roundtrip', str(data)], cwd=ROOT, env=dict(os.environ, DOTNET_TieredCompilation='1', DOTNET_TieredPGO='1'), capture_output=True, text=True, timeout=300)
        (OUTPUT / 'execution.log').write_text(child.stdout + child.stderr)
        hefty.evidence.require_success(child, 'Physical round-trip workload')
        raw = json.loads(child.stdout); fixtures = validate(raw)
        hefty.validate_manifest({'Schema': 'csv-hefty-fixtures/0.1', 'Fixtures': fixtures},
                                {'cases': [dict(id=i, rows=50_000, noteCharacters=128, unicode=True, culture=c, invalidEvery=0) for i, c in [('roundtrip-invariant', ''), ('roundtrip-german', 'de-DE')]], 'maximumTotalFileBytes': 1024 * 1024 * 1024}, data)
        for f in fixtures:
            if hefty.file_sha(data / f['FileName']) != f['FileSha256']:
                raise ValueError('Physical input differs from its hash.')
            for pipeline in ('Original', 'Assayed'):
                output = data / (f['Specification']['Id'] + '-' + pipeline + '.output.csv')
                row = next(r for r in raw['samples'] if r['caseId'] == f['Specification']['Id'] and r['pipeline'] == pipeline)
                if hefty.file_sha(output) != row['outputSha256'] or output.stat().st_size != row['outputBytes']:
                    raise ValueError('Physical output differs from its measured checksum.')
        hefty.evidence.assert_bindings(bindings)
        if hefty.evidence.artifacts(hefty.SAMPLE) != artifacts or hefty.file_sha(hefty.DLL.with_name('CsvHelper.dll')) != package_hash:
            raise ValueError('Compiled artifacts changed during the workload.')
        summaries = []
        for f in fixtures:
            for pipeline in ('Original', 'Assayed'):
                rows = [r for r in raw['samples'] if r['caseId'] == f['Specification']['Id'] and r['pipeline'] == pipeline and not r['warmup']]
                summaries.append({'case': f['Specification']['Id'], 'pipeline': pipeline,
                                  'medianMilliseconds': round(statistics.median(r['elapsedMilliseconds'] for r in rows), 3),
                                  'medianAllocatedBytes': statistics.median(r['allocatedBytes'] for r in rows)})
        report.update(executionStatus='completed', raw=raw, summaries=summaries, inputSha256=bindings, artifactSha256=artifacts, csvHelperAssemblySha256=package_hash, validationSha256=hefty.evidence.sha(OUTPUT.parent / 'validation.json'))
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as failure:
        report.update(executionStatus='failed', failure=str(failure)); hefty.evidence.write(OUTPUT / 'comparison.json', report)
        raise
    hefty.evidence.write(OUTPUT / 'comparison.json', report)
    print(json.dumps({'executionStatus': report['executionStatus'], 'summaries': report['summaries']}, indent=2))


if __name__ == '__main__':
    main()
