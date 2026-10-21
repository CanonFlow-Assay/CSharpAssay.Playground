#!/usr/bin/env python3
"""Measure order-sample coverage and mutation uplift, with optional bounded advice."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RELATIVE = Path('samples/40-functional-shape-v0.1')
SAMPLE = ROOT / RELATIVE
OUTPUT = ROOT / 'evidence/generated/shape-orders'
MANIFEST = SAMPLE / 'mutation-cases.json'
TEST_PROJECT = Path('tests/Shape.Behavior.Tests/Shape.Behavior.Tests.csproj')
TEST_DLL = Path('tests/Shape.Behavior.Tests/bin/Release/net11.0/Shape.Behavior.Tests.dll')
CLI = ROOT / 'samples/50-decision-assistance/src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'
BUILD_FILES = ('global.json', 'Directory.Build.props', 'Directory.Build.targets', '.editorconfig', 'Directory.Packages.props')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def run(argv, cwd, log):
    result = subprocess.run([str(arg) for arg in argv], cwd=cwd, text=True,
                            capture_output=True, timeout=180)
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_text(result.stdout + result.stderr + '\nprocessExitCode=' + str(result.returncode) + '\n')
    return result


def require_success(result, operation):
    if result.returncode:
        raise RuntimeError(operation + ' failed (exit ' + str(result.returncode) + '); inspect its generated log.')


def test_record(path, exit_code, expected):
    assemblies = ET.parse(path).getroot().findall('assembly')
    if len(assemblies) != 1:
        raise ValueError('Expected one executed test assembly.')
    assembly = assemblies[0]
    counts = {key: int(assembly.get(key, '-1')) for key in ('total', 'passed', 'failed', 'skipped', 'errors')}
    if (counts['total'] != expected or counts['skipped'] or counts['errors'] or
            counts['passed'] + counts['failed'] != counts['total'] or
            exit_code not in (0, 2) or (exit_code == 0) != (counts['failed'] == 0)):
        raise ValueError('Missing, incomplete, inconsistent, or unexpected test execution.')
    tests = assembly.findall('./collection/test')
    if len(tests) != expected or any(test.get('result') not in ('Pass', 'Fail') for test in tests):
        raise ValueError('Test details do not establish the declared selected suite.')
    return {**counts, 'exitCode': exit_code,
            'failedTests': sorted(test.get('name') for test in tests if test.get('result') == 'Fail'),
            'reportSha256': sha(path)}


def coverage_record(path):
    root = ET.parse(path).getroot()
    modules = root.findall('./packages/package')
    if {p.get('name') for p in modules} != {'Shape.Domain', 'Shape.Application', 'Shape.Api', 'Shape.Infrastructure'}:
        raise ValueError('Coverage must include all four production assemblies.')
    counts = {key: int(root.attrib[key]) for key in ('lines-covered', 'lines-valid', 'branches-covered', 'branches-valid')}
    if (counts['lines-valid'] <= 0 or counts['branches-valid'] <= 0 or
            not 0 <= counts['lines-covered'] <= counts['lines-valid'] or
            not 0 <= counts['branches-covered'] <= counts['branches-valid']):
        raise ValueError('Empty coverage evidence.')
    hits = {}
    for line_class in root.findall('./packages/package/classes/class'):
        filename = line_class.attrib['filename'].replace('\\', '/')
        if '/src/' not in filename:
            raise ValueError('Unexpected coverage source scope.')
        relative = 'src/' + filename.rsplit('/src/', 1)[1]
        for line in line_class.findall('./lines/line'):
            key = relative + ':' + line.attrib['number']
            hits[key] = max(hits.get(key, 0), int(line.attrib['hits']))
    return {**counts, 'linePercent': round(100 * counts['lines-covered'] / counts['lines-valid'], 2),
            'branchPercent': round(100 * counts['branches-covered'] / counts['branches-valid'], 2),
            'assemblies': {p.attrib['name']: {'linePercent': round(float(p.attrib['line-rate']) * 100, 2),
                                             'branchPercent': round(float(p.attrib['branch-rate']) * 100, 2)} for p in modules},
            'reportSha256': sha(path), 'lineHits': hits}


def tests(sample, folder, count, selected_class=None, coverage=False):
    folder.mkdir(parents=True, exist_ok=True)
    # Every invocation must create fresh evidence, including after an earlier
    # interrupted run left a plausible XML file in this output directory.
    (folder / 'tests.xml').unlink(missing_ok=True)
    if coverage:
        (folder / 'coverage.cobertura.xml').unlink(missing_ok=True)
    argv = ['dotnet', sample / TEST_DLL, '--minimum-expected-tests', str(count), '--seed', '42',
            '--report-xunit', '--report-xunit-filename', 'tests.xml', '--results-directory', folder]
    if selected_class:
        argv.extend(['--filter-class', selected_class])
    if coverage:
        argv.extend(['--coverage', '--coverage-output', folder / 'coverage.cobertura.xml',
                     '--coverage-output-format', 'cobertura', '--coverage-settings', sample / 'coverage.config'])
    result = run(argv, sample, folder / 'execution.log')
    return test_record(folder / 'tests.xml', result.returncode, count)


def advice(packet):
    result = subprocess.run(['dotnet', str(CLI), str(packet)], cwd=ROOT, text=True, capture_output=True, timeout=40)
    require_success(result, 'Offline advice')
    value = json.loads(result.stdout)
    if value.get('verificationVerdict') != 'Inconclusive' or value.get('schema') != 'playground-decision-advice/0.1':
        raise ValueError('Advice changed the experiment authority boundary.')
    return value


def compare_providers(packets, providers, maximum):
    # Called only after explicit --live. Credentials are presence-checked, never printed.
    report = {'schema': 'shape-advice-comparison/0.1', 'maxRequests': maximum,
              'requestsAttempted': 0, 'results': [], 'providers': {}, 'qualityGateApplied': False,
              'authority': 'Small agent-reviewed sample comparison; not provider qualification.'}
    available = []
    for provider in providers:
        configured = bool(os.environ.get({'jev': 'TYPESAFE_API_KEY', 'openai': 'OPENAI_API_KEY'}[provider]))
        report['providers'][provider] = 'configured' if configured else 'unavailable_missing_key'
        if configured:
            available.append(provider)
    if len(packets) * len(available) > maximum:
        raise ValueError('Comparison exceeds the declared request budget; no calls made.')
    stopped = False
    for packet in packets:
        baseline = advice(packet)
        if baseline['adviceStatus'] != 'suggested':
            continue
        for provider in available:
            if stopped:
                report['results'].append({'packet': str(packet.relative_to(OUTPUT)), 'provider': provider, 'status': 'not_run_after_failure'})
                continue
            report['requestsAttempted'] += 1
            # Persist budget use before invocation, so an interruption cannot erase it.
            write(OUTPUT / 'provider-comparison.json', report)
            try:
                result = subprocess.run(['dotnet', str(CLI), str(packet), '--provider=' + provider],
                                        cwd=ROOT, capture_output=True, text=True, timeout=40)
                require_success(result, 'Provider CLI')
                value = json.loads(result.stdout)
                valid = value['verificationVerdict'] == 'Inconclusive'
                row = {'packet': str(packet.relative_to(OUTPUT)), 'provider': provider,
                       'packetSha256': sha(packet), 'baseline': baseline, 'advice': value,
                       'matchesAgentExpectation': value['action'] == baseline['action'],
                       'verdictPreserved': valid, 'status': 'completed'}
                stopped = not valid or value['adviceStatus'] == 'unavailable'
            except (RuntimeError, ValueError, OSError, KeyError, subprocess.TimeoutExpired):
                row = {'packet': str(packet.relative_to(OUTPUT)), 'provider': provider, 'status': 'execution_failed'}
                stopped = True
            report['results'].append(row)
            write(OUTPUT / 'provider-comparison.json', report)
    report['executionStatus'] = 'stopped_after_failure' if stopped else ('completed' if available else 'unavailable_missing_keys')
    write(OUTPUT / 'provider-comparison.json', report)
    return report['executionStatus']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--provider', action='append', choices=('jev', 'openai'))
    parser.add_argument('--live', action='store_true')
    parser.add_argument('--max-requests', type=int, default=8)
    args = parser.parse_args()
    if args.provider and not args.live:
        parser.error('Explicit --live is required for providers.')
    if not 1 <= args.max_requests <= 16 or len(args.provider or []) != len(set(args.provider or [])):
        parser.error('Choose distinct providers and a request budget from 1 to 16.')
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'shape-order-investigations/0.1', 'executionStatus': 'running', 'synthetic': True,
              'executed': True, 'qualityGateApplied': False, 'results': [],
              'authority': 'Executed order-sample evidence; separate from authoritative Assay verification.'}
    write(OUTPUT / 'investigations.json', report)
    current = None
    try:
        corpus = json.loads(MANIFEST.read_text())
        if corpus['schema'] != 'shape-order-mutation-corpus/0.1' or corpus['synthetic'] is not True:
            raise ValueError('Unsupported corpus.')
        ids = [case['id'] for case in corpus['cases']]
        if len(ids) != len(set(ids)) or not 1 <= len(ids) <= 40 or any(not re.fullmatch('[a-z0-9-]+', value) for value in ids):
            raise ValueError('Corpus must have 1..40 unique, safe case identifiers.')
        bound = [MANIFEST, Path(__file__), CLI, SAMPLE / 'coverage.config',
                 CLI.with_name('DecisionAssist.Domain.dll'), CLI.with_suffix('.deps.json'),
                 CLI.with_suffix('.runtimeconfig.json'),
                 *[p for p in (ROOT / 'samples/50-decision-assistance/src').rglob('*.cs')
                   if not {'bin', 'obj'}.intersection(p.parts)],
                 *[ROOT / name for name in BUILD_FILES],
                 *[p for p in SAMPLE.rglob('*') if p.is_file() and p.suffix in ('.cs', '.csproj', '.slnx', '.json') and not {'bin','obj','TestResults','evaluation'}.intersection(p.relative_to(SAMPLE).parts)]]
        report['inputSha256'] = {str(path.relative_to(ROOT)): sha(path) for path in set(bound)}
        report['reviewStatus'] = corpus['reviewStatus']
        report['sdk'] = subprocess.check_output(['dotnet', '--version'], cwd=ROOT, text=True).strip()
        if report['sdk'] != json.loads((ROOT / 'global.json').read_text())['sdk']['version']:
            raise ValueError('Exact pinned SDK required.')
        count = json.loads((SAMPLE / 'expected-evidence.json').read_text())['tests']['Shape.Behavior.Tests']
        baseline_count = 16
        before_packets = []
        after_packets = []
        # Collect on the ordinary checkout build: this pinned collector produces
        # empty instrumentation for the disposable PathMap build on this host.
        # All production/test source inputs are bound; isolated originals below
        # must independently pass the same selections before any mutation runs.
        # The preceding package qualification gate uses a disposable NuGet
        # cache; restore asset paths into this checkout's normal cache first.
        require_success(run(['dotnet', 'restore', SAMPLE / 'Shape.slnx', '--locked-mode'],
                            ROOT, OUTPUT / 'coverage-restore.log'), 'Coverage baseline restore')
        require_success(run(['dotnet', 'build', SAMPLE / TEST_PROJECT, '--no-restore', '-c', 'Release'],
                            ROOT, OUTPUT / 'coverage-build.log'), 'Coverage baseline build')
        for phase, selected, expected in [('before', corpus['baselineClass'], baseline_count), ('after', None, count)]:
            folder = OUTPUT / 'baseline' / phase
            observed = tests(SAMPLE, folder, expected, selected, coverage=True)
            if observed['failed']:
                raise RuntimeError('Original baseline tests must pass before mutation.')
            report.setdefault('baselines', {})[phase] = observed
            report.setdefault('coverage', {})[phase] = coverage_record(folder / 'coverage.cobertura.xml')
        if any(report['coverage']['before'][key] != report['coverage']['after'][key]
               for key in ('lines-valid', 'branches-valid')):
            raise ValueError('Coverage selections have different denominators; uplift is not comparable.')
        with tempfile.TemporaryDirectory(prefix='csassay-order-cases-') as temporary:
            scratch = Path(temporary)
            for name in BUILD_FILES:
                shutil.copyfile(ROOT / name, scratch / name)
            target = scratch / RELATIVE
            shutil.copytree(SAMPLE, target, ignore=shutil.ignore_patterns('bin','obj','TestResults','.git','evaluation'))
            # Sources and package cache are explicit and local to this checkout.
            config = ET.Element('configuration')
            settings = ET.SubElement(config, 'config')
            ET.SubElement(settings, 'add', key='globalPackagesFolder', value=str(ROOT / '.packages/cache'))
            sources = ET.SubElement(config, 'packageSources')
            ET.SubElement(sources, 'clear')
            ET.SubElement(sources, 'add', key='assay-local', value=str(ROOT / '.packages'))
            ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
            ET.ElementTree(config).write(scratch / 'NuGet.config', encoding='utf-8', xml_declaration=True)
            require_success(run(['dotnet','restore',target / 'Shape.slnx','--locked-mode'], scratch, OUTPUT / 'restore.log'), 'Isolated restore')
            def build(log):
                return run(['dotnet','build', target / TEST_PROJECT,'--no-restore','-c','Release','-m:1'], scratch, log)
            require_success(build(OUTPUT / 'original-build.log'), 'Original build')
            for phase, selected, expected in [('before', corpus['baselineClass'], baseline_count), ('after', None, count)]:
                folder = OUTPUT / 'isolated-baseline' / phase
                observed = tests(target, folder, expected, selected)
                if observed['failed']:
                    raise RuntimeError('Isolated original baseline must independently pass.')
                report.setdefault('isolatedBaselines', {})[phase] = observed
            for case in corpus['cases']:
                current = case['id']
                relative = Path(case['file'])
                if not str(relative).startswith('src/') or '..' in relative.parts or relative.suffix != '.cs':
                    raise ValueError('Mutation target outside registered sample source.')
                path = target / relative
                original = path.read_bytes()
                before = case['before'].encode()
                if original.count(before) != 1:
                    raise ValueError('Mutation must target exactly one reviewed source span: ' + current)
                changed = original.replace(before, case['after'].encode())
                line = original[:original.index(before)].count(b'\n') + 1
                folder = OUTPUT / 'cases' / current
                path.write_bytes(changed)
                try:
                    require_success(build(folder / 'build.log'), 'Mutation compilation for ' + current)
                    observed_before = tests(target, folder / 'before', baseline_count, corpus['baselineClass'])
                    observed_after = tests(target, folder / 'after', count)
                finally:
                    path.write_bytes(original)
                outcome_before = 'survived' if not observed_before['failed'] else 'killed'
                outcome_after = 'survived' if not observed_after['failed'] else 'killed'
                if outcome_after != case['expectedAfter']:
                    raise RuntimeError('After outcome disagrees with reviewed expectation for ' + current)
                row = {'id': current, 'area': case['area'], 'requirement': case['requirement'],
                       'sourceSha256': hashlib.sha256(original).hexdigest(), 'mutantSha256': hashlib.sha256(changed).hexdigest(),
                       'location': case['file'] + ':' + str(line), 'before': observed_before, 'after': observed_after,
                       'outcomeBefore': outcome_before, 'outcomeAfter': outcome_after,
                       'gapDetectedByAddedTests': outcome_before == 'survived' and outcome_after == 'killed',
                       'survivorDisposition': 'unresolved' if outcome_after == 'survived' else 'killed_by_declared_tests'}
                for phase, observation, outcome in [('before', observed_before, outcome_before), ('after', observed_after, outcome_after)]:
                    coverage_hits = report['coverage'][phase]['lineHits'].get(row['location'])
                    packet = {'schema': 'playground-mutation-experiment/0.1', 'synthetic': True,
                              'task': 'functional-shape-order-investigation',
                              'investigationContext': case['requirement'],
                              'sourceSha256': row['sourceSha256'], 'caseId': current, 'selectedSuite': phase,
                              'baselinePassed': True, 'mutantCompiled': True, 'testsCompleted': True,
                              'survived': outcome == 'survived', 'covered': None if coverage_hits is None else coverage_hits > 0,
                              'hasBoundaryAssertion': case['beforeHasAssertion'] if phase == 'before' else True,
                              'coverageBasis': 'Cobertura executable-line evidence on original source for selected suite; not mutant branch coverage.',
                              'assertionBasis': 'Agent-reviewed declared requirement assertions; not automatic assertion analysis.',
                              'testEvidence': observation, 'reviewStatus': corpus['reviewStatus'],
                              'survivorDisposition': 'unresolved' if outcome == 'survived' else 'killed_by_selected_tests',
                              'inputSha256': report['inputSha256']}
                    packet_path = folder / phase / 'experiment.json'
                    write(packet_path, packet)
                    result = advice(packet_path)
                    write(folder / phase / 'advice.json', result)
                    row[phase + 'Advice'] = result
                    if result['adviceStatus'] == 'suggested':
                        (before_packets if phase == 'before' else after_packets).append(packet_path)
                report['results'].append(row)
                write(OUTPUT / 'investigations.json', report)
                print(current + ': ' + outcome_before + ' -> ' + outcome_after, flush=True)
        if any(sha(ROOT / path) != value for path, value in report['inputSha256'].items()):
            raise ValueError('Bound input changed during execution.')
        report['executionStatus'] = 'completed'
        report['summary'] = {'cases': len(report['results']), 'baselineTests': baseline_count, 'upliftTests': count,
                             'killedBefore': sum(row['outcomeBefore']=='killed' for row in report['results']),
                             'killedAfter': sum(row['outcomeAfter']=='killed' for row in report['results']),
                             'newlyDetected': sum(row['gapDetectedByAddedTests'] for row in report['results']),
                             'unresolvedSurvivors': [row['id'] for row in report['results'] if row['outcomeAfter']=='survived']}
        write(OUTPUT / 'investigations.json', report)
        if args.provider:
            # Three distinct pre-repair gaps plus one unresolved post-uplift survivor.
            packets = before_packets[:3] + after_packets[:1]
            report['providerComparison'] = compare_providers(packets, args.provider, args.max_requests)
        else:
            report['providerComparison'] = 'not_requested_offline_run'
        write(OUTPUT / 'investigations.json', report)
        print(json.dumps({'summary': report['summary'], 'coverageBefore': report['coverage']['before']['linePercent'],
                          'coverageAfter': report['coverage']['after']['linePercent'], 'providers': report['providerComparison']}))
    except (ValueError, RuntimeError, OSError, KeyError, TypeError, ET.ParseError, subprocess.TimeoutExpired, KeyboardInterrupt) as error:
        report.update(executionStatus='failed', failedCase=current, failureType=type(error).__name__, message=str(error)[:300])
        write(OUTPUT / 'investigations.json', report)
        raise


if __name__ == '__main__':
    main()
