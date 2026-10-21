#!/usr/bin/env python3
"""Verify the functional CsvHelper sample, coverage, isolated mutations, and offline advice."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/60-csvhelper-functional'
OUTPUT = ROOT / 'evidence/generated/csvhelper/functional'
TEST_DLL = Path('tests/CsvFunctional.Tests/bin/Release/net11.0/CsvFunctional.Tests.dll')
SOLUTION = 'CsvFunctional.slnx'
ASSIST = ROOT / 'samples/50-decision-assistance/src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'
BUILD_FILES = ('global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', '.editorconfig')
spec = importlib.util.spec_from_file_location('shape_evidence', ROOT / 'eng/run-shape-investigations.py')
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)
sha, write, run, require_success, test_record = helpers.sha, helpers.write, helpers.run, helpers.require_success, helpers.test_record


def coverage_record(path):
    root = ET.parse(path).getroot()
    modules = root.findall('./packages/package')
    if {p.get('name') for p in modules} != {'CsvFunctional.Domain', 'CsvFunctional.Adapter'}:
        raise ValueError('Both production core and adapter assemblies must have coverage evidence.')
    counts = {k: int(root.attrib[k]) for k in ('lines-covered', 'lines-valid', 'branches-covered', 'branches-valid')}
    if any(counts[k] <= 0 for k in ('lines-valid', 'branches-valid')) or any(
            not 0 <= counts[p + '-covered'] <= counts[p + '-valid'] for p in ('lines', 'branches')):
        raise ValueError('Invalid or empty coverage.')
    hits = {}
    for cls in root.findall('./packages/package/classes/class'):
        name = cls.attrib['filename'].replace('\\', '/')
        if '/src/' not in name:
            raise ValueError('Unexpected coverage scope.')
        relative = 'src/' + name.rsplit('/src/', 1)[1]
        for line in cls.findall('./lines/line'):
            key = relative + ':' + line.attrib['number']
            hits[key] = max(hits.get(key, 0), int(line.attrib['hits']))
    return {**counts, 'linePercent': round(100 * counts['lines-covered'] / counts['lines-valid'], 2),
            'branchPercent': round(100 * counts['branches-covered'] / counts['branches-valid'], 2),
            'lineHits': hits, 'reportSha256': sha(path),
            'scope': 'All Domain and Adapter code, including generated record/union members; CLI tested separately.'}


def tests(sample, folder, count, before=False, coverage=False):
    folder.mkdir(parents=True, exist_ok=True)
    for name in ('tests.xml', 'coverage.cobertura.xml'):
        (folder / name).unlink(missing_ok=True)
    argv = ['dotnet', sample / TEST_DLL, '--minimum-expected-tests', str(count), '--seed', '42',
            '--report-xunit', '--report-xunit-filename', 'tests.xml', '--results-directory', folder]
    if before:
        argv += ['--filter-class', 'CsvFunctional.Tests.SmokeTests']
    if coverage:
        argv += ['--coverage', '--coverage-output', folder / 'coverage.cobertura.xml',
                 '--coverage-output-format', 'cobertura', '--coverage-settings', sample / 'coverage.config']
    result = run(argv, sample, folder / 'execution.log')
    return test_record(folder / 'tests.xml', result.returncode, count)


def exact_edit(text, case):
    if text.count(case['before']) != 1:
        raise ValueError('Mutation must identify exactly one source span: ' + case['id'])
    return text.replace(case['before'], case['after'], 1)


def input_bindings():
    bound = [Path(__file__), ROOT / 'eng/run-shape-investigations.py', ROOT / 'NuGet.config',
             *[ROOT / p for p in BUILD_FILES],
             *[p for p in SAMPLE.rglob('*') if p.is_file() and p.suffix in ('.cs', '.csproj', '.slnx', '.json', '.config', '.csv', '.txt') and not {'bin', 'obj', 'TestResults'}.intersection(p.relative_to(SAMPLE).parts)],
             *[p for p in ASSIST.parent.iterdir() if p.suffix in ('.dll', '.json')],
             *[p for p in (ROOT / 'samples/50-decision-assistance/src').rglob('*.cs') if not {'bin', 'obj'}.intersection(p.parts)]]
    return {str(p.relative_to(ROOT)): sha(p) for p in set(bound)}


def artifacts(sample):
    return {str(p.relative_to(sample)): sha(p) for p in sample.rglob('*')
            if p.is_file() and 'bin/Release/net11.0/' in p.as_posix()
            and p.name.startswith('CsvFunctional.') and p.suffix in ('.dll', '.pdb', '.json')}


def assert_bindings(bindings):
    if any(not (ROOT / name).is_file() or sha(ROOT / name) != value for name, value in bindings.items()):
        raise ValueError('Bound inputs changed during execution; discard this completion claim.')


def advice(packet):
    result = run(['dotnet', ASSIST, packet], ROOT, packet.with_suffix('.advice.log'))
    require_success(result, 'Offline Decision Assist')
    value = json.loads(result.stdout)
    if value.get('verificationVerdict') != 'Inconclusive' or value.get('provider') != 'deterministic-baseline':
        raise ValueError('Advice exceeded its authority or called an unrequested provider.')
    write(packet.with_suffix('.advice.json'), value)
    return value


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'csv-functional-investigations/0.1', 'executionStatus': 'running', 'results': [],
              'synthetic': True, 'executed': True, 'providerRequests': 0, 'qualityGateApplied': False,
              'reviewStatus': 'Agent review; human adjudication pending.',
              'authority': 'Local sample experiment; separate from authoritative verify receipts and upstream baseline.'}
    write(OUTPUT / 'investigations.json', report)
    try:
        sdk = subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip()
        if sdk != json.loads((ROOT / 'global.json').read_text())['sdk']['version']:
            raise ValueError('The exact pinned SDK is required.')
        report['sdk'] = sdk
        corpus = json.loads((SAMPLE / 'mutation-cases.json').read_text())
        if corpus['schema'] != 'csv-functional-mutations/0.1' or corpus['synthetic'] is not True:
            raise ValueError('Unsupported mutation manifest.')
        ids = [c['id'] for c in corpus['cases']]
        if not 1 <= len(ids) <= 30 or len(ids) != len(set(ids)) or any(not re.fullmatch('[a-z0-9-]+', i) for i in ids):
            raise ValueError('Expected 1..30 distinct safe mutation identifiers.')
        for case in corpus['cases']:
            target = (SAMPLE / case['path']).resolve()
            if not target.is_relative_to(SAMPLE / 'src') or target.suffix != '.cs' or case['expectedAfter'] not in ('killed', 'survived'):
                raise ValueError('Mutation scope must remain an explicit production C# source span.')
            text = target.read_text()
            exact_edit(text, case)
            if text[:text.index(case['before'])].count('\n') + 1 != case['line']:
                raise ValueError('Reviewed line binding changed.')
        require_success(run(['dotnet', 'restore', SAMPLE / SOLUTION, '--locked-mode'], ROOT, OUTPUT / 'restore.log'), 'Locked restore')
        require_success(run(['dotnet', 'build', SAMPLE / SOLUTION, '--no-restore', '-c', 'Release'], ROOT, OUTPUT / 'build.log'), 'Build')
        require_success(run(['dotnet', 'build', ASSIST.parents[3] / 'DecisionAssist.Cli.csproj', '-c', 'Release'], ROOT, OUTPUT / 'assist-build.log'), 'Decision Assist build')
        report['inputSha256'] = input_bindings()
        count = json.loads((SAMPLE / '.csassay.json').read_text())['release']['tests'][0]['minimumExpectedTests']
        report['baselineArtifactSha256'] = artifacts(SAMPLE)
        report['selections'] = {'before': 'Two authored smoke tests; not upstream tests.', 'beforeTests': 2, 'afterTests': count}
        report['baselines'] = {}
        report['coverage'] = {}
        for phase, expected in (('before', 2), ('after', count)):
            folder = OUTPUT / 'baseline' / phase
            result = tests(SAMPLE, folder, expected, before=phase == 'before', coverage=True)
            if result['failed']:
                raise RuntimeError('Original sample baseline failed.')
            report['baselines'][phase] = result
            report['coverage'][phase] = coverage_record(folder / 'coverage.cobertura.xml')
        for measure in ('lines-valid', 'branches-valid'):
            if report['coverage']['before'][measure] != report['coverage']['after'][measure]:
                raise ValueError('Coverage scopes changed between selections.')
        cli = SAMPLE / 'src/CsvFunctional.Cli/bin/Release/net11.0/CsvFunctional.Cli.dll'
        demo = run(['dotnet', cli, SAMPLE / 'fixtures/mixed.csv'], ROOT, OUTPUT / 'cli-demo.log')
        events = [json.loads(line) for line in demo.stdout.splitlines()]
        if demo.returncode != 2 or [e['status'] for e in events] != ['accepted', 'rejected', 'accepted'] or events[0]['Note'] != 'hello, csv' or events[2]['Note'] != 'quote "inside"' or len(events[1]['issues']) != 3:
            raise ValueError('CLI integration projection failed.')
        report['cli'] = {'exitCode': demo.returncode, 'events': events, 'logSha256': sha(OUTPUT / 'cli-demo.log')}
        with tempfile.TemporaryDirectory(prefix='csassay-csv-') as temporary:
            scratch = Path(temporary)
            copy = scratch / 'sample'
            shutil.copytree(SAMPLE, copy, ignore=shutil.ignore_patterns('bin', 'obj', 'TestResults'))
            for name in BUILD_FILES:
                shutil.copyfile(ROOT / name, scratch / name)
            require_success(run(['dotnet', 'restore', copy / SOLUTION, '--locked-mode', '--configfile', ROOT / 'NuGet.config'], scratch, OUTPUT / 'isolated-restore.log'), 'Isolated restore')
            require_success(run(['dotnet', 'build', copy / SOLUTION, '--no-restore', '-c', 'Release'], scratch, OUTPUT / 'isolated-build.log'), 'Isolated baseline build')
            for phase, expected in (('before', 2), ('after', count)):
                if tests(copy, OUTPUT / 'isolated-baseline' / phase, expected, before=phase == 'before')['failed']:
                    raise RuntimeError('Isolated original baseline failed.')
            for case in corpus['cases']:
                target = copy / case['path']
                original = target.read_bytes()
                folder = OUTPUT / 'mutations' / case['id']
                try:
                    target.write_text(exact_edit(original.decode(), case))
                    build = run(['dotnet', 'build', copy / SOLUTION, '--no-restore', '-c', 'Release'], scratch, folder / 'build.log')
                    require_success(build, 'Declared valid mutant ' + case['id'])
                    row = {'id': case['id'], 'requirement': case['requirement'], 'sourceSha256': sha(target), 'artifactSha256': artifacts(copy), 'buildLogSha256': sha(folder / 'build.log'), 'phases': {}}
                    for phase, expected in (('before', 2), ('after', count)):
                        execution = tests(copy, folder / phase, expected, before=phase == 'before')
                        survived = execution['failed'] == 0
                        line = case['path'] + ':' + str(case['line'])
                        hits = report['coverage'][phase]['lineHits']
                        covered = hits[line] > 0 if line in hits else None
                        packet = {'schema': 'playground-mutation-experiment/0.1', 'synthetic': True,
                                  'task': 'csvhelper-functional-investigation', 'investigationContext': case['requirement'],
                                  'sourceSha256': row['sourceSha256'], 'baselinePassed': True, 'mutantCompiled': True,
                                  'testsCompleted': True, 'survived': survived, 'covered': covered,
                                  'hasBoundaryAssertion': phase == 'after',
                                  'assertionEvidence': 'Agent-reviewed declared requirement coverage; human adjudication pending.',
                                  'baselineTestReportSha256': report['baselines'][phase]['reportSha256'],
                                  'mutantExecution': execution, 'mutantArtifactSha256': row['artifactSha256'], 'buildLogSha256': row['buildLogSha256'], 'originalCoverageReportSha256': report['coverage'][phase]['reportSha256']}
                        packet_path = folder / phase / 'experiment.json'
                        write(packet_path, packet)
                        row['phases'][phase] = {'state': 'survived' if survived else 'killed', 'testExecution': execution,
                                                'covered': covered, 'advice': advice(packet_path)}
                    report['results'].append(row)
                    write(OUTPUT / 'investigations.json', report)
                    if row['phases']['after']['state'] != case['expectedAfter']:
                        raise RuntimeError('Observed outcome differs from reviewed expectation: ' + case['id'])
                finally:
                    target.write_bytes(original)
        assert_bindings(report['inputSha256'])
        if input_bindings() != report['inputSha256']:
            raise ValueError('Input inventory changed during execution.')
        report['summary'] = {phase: {'killed': sum(r['phases'][phase]['state'] == 'killed' for r in report['results']),
                                      'survived': sum(r['phases'][phase]['state'] == 'survived' for r in report['results'])} for phase in ('before', 'after')}
        report['executionStatus'] = 'completed'
    except (OSError, ValueError, RuntimeError, KeyError, ET.ParseError, subprocess.SubprocessError) as failure:
        report['executionStatus'] = 'failed'
        report['failure'] = str(failure)
        write(OUTPUT / 'investigations.json', report)
        raise
    write(OUTPUT / 'investigations.json', report)
    print(json.dumps({'executionStatus': report['executionStatus'], 'summary': report['summary'], 'coverage': {p: {k:v for k,v in x.items() if k not in ('lineHits',)} for p,x in report['coverage'].items()}}, indent=2))


if __name__ == '__main__':
    main()
