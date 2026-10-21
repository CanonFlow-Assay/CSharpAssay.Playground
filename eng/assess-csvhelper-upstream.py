#!/usr/bin/env python3
"""Assess the pinned upstream source separately from the functional consumer sample.

Default: validate and summarize existing local execution. --run executes the
original net9 test target with Major runtime roll-forward. --fetch permits a
single bounded exact-commit fetch only when the source cache is absent.
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/60-csvhelper-functional'
OUTPUT = ROOT / 'evidence/generated/csvhelper'
UPSTREAM = OUTPUT / 'upstream'
CACHE = ROOT / '.upstream-cache/CsvHelper'
ASSAY = ROOT.parent / 'CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll'
ASSIST = ROOT / 'samples/50-decision-assistance/src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def execute(argv, cwd, log, env=None):
    result = subprocess.run([str(x) for x in argv], cwd=cwd, env=env, text=True, capture_output=True, timeout=300)
    log.write_text(result.stdout + result.stderr + '\nprocessExitCode=' + str(result.returncode) + '\n')
    return result.returncode


def read_tests(path):
    namespace = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tree = ET.parse(path).getroot()
    node = tree.find('t:ResultSummary/t:Counters', namespace)
    counts = {k: int(node.attrib[k]) for k in ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'notExecuted')}
    results = tree.findall('t:Results/t:UnitTestResult', namespace)
    if (counts['total'] != 1063 or counts['executed'] != counts['total'] or
            counts['passed'] + counts['failed'] != counts['total'] or
            any(counts[k] for k in ('error', 'timeout', 'aborted', 'notExecuted')) or len(results) != counts['total']):
        raise ValueError('Incomplete or changed upstream test selection; do not claim this baseline.')
    failed = sorted(r.attrib['testName'] for r in results if r.attrib['outcome'] == 'Failed')
    if len(failed) != counts['failed']:
        raise ValueError('Test failures are not accounted for.')
    return {**counts, 'baselinePassed': counts['failed'] == 0, 'failedTests': failed, 'reportSha256': sha(path),
            'targetFramework': 'net9.0', 'runtimePolicy': 'DOTNET_ROLL_FORWARD=Major; actual test-host runtime revision not observed',
            'scope': 'Original CsvHelper.Tests, one Linux target; not the upstream Windows/multi-TFM CI matrix.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--fetch', action='store_true')
    args = parser.parse_args()
    pin = json.loads((SAMPLE / 'upstream.json').read_text())
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'csvhelper-upstream-assessment/0.1', 'executionStatus': 'running', 'source': pin,
              'providerRequests': 0, 'authority': 'Local port assessment; not upstream qualification or a whole-library migration.'}
    write(OUTPUT / 'upstream-assessment.json', report)
    try:
        if not CACHE.exists():
            if not args.fetch:
                raise ValueError('Pinned local checkout missing. Explicit --fetch permits one exact-commit fetch.')
            CACHE.mkdir(parents=True)
            subprocess.run(['git', 'init', '-q', CACHE], check=True)
            code = execute(['git', 'fetch', '--depth=1', pin['repository'], pin['commit']], CACHE, OUTPUT / 'fetch.log')
            if code:
                raise RuntimeError('Fetch stopped. Inspect response; do not retry or bypass restrictions.')
            subprocess.run(['git', 'checkout', '--detach', '-q', 'FETCH_HEAD'], cwd=CACHE, check=True)
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=CACHE, text=True).strip()
        if commit != pin['commit'] or subprocess.check_output(['git', 'status', '--porcelain'], cwd=CACHE, text=True).strip():
            raise ValueError('Upstream checkout must remain clean at the pinned commit.')
        paths = subprocess.check_output(['git', 'ls-files', '-z'], cwd=CACHE).decode().split('\0')
        paths = [p for p in paths if p.startswith(('src/CsvHelper/', 'tests/CsvHelper.Tests/')) or p in ('LICENSE.txt', 'README.markdown', '.editorconfig')]
        source = {name: sha(CACHE / name) for name in paths}
        for name, expected in pin['derivedFiles'].items():
            if source[name] != expected:
                raise ValueError('Upstream-derived algorithm identity changed.')
        report['sourceSha256'] = source
        if args.run:
            if UPSTREAM.exists():
                shutil.rmtree(UPSTREAM)
            UPSTREAM.mkdir()
            blob = subprocess.check_output(['git', 'archive', 'HEAD'], cwd=CACHE)
            with tarfile.open(fileobj=io.BytesIO(blob)) as archive:
                archive.extractall(UPSTREAM, filter='data')
            (UPSTREAM / 'global.json').write_text(json.dumps({'sdk': json.loads((ROOT / 'global.json').read_text())['sdk']}))
            for name in ('Directory.Build.props', 'Directory.Build.targets'):
                (UPSTREAM / name).write_text('<Project />\n')
            (UPSTREAM / 'Directory.Packages.props').write_text('<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>\n')
            (UPSTREAM / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
            project = 'tests/CsvHelper.Tests/CsvHelper.Tests.csproj'
            for operation, argv in (
                ('restore', ['dotnet', 'restore', project, '-p:TargetFrameworks=net9.0', '--configfile', 'NuGet.Config']),
                ('build', ['dotnet', 'build', project, '-p:TargetFrameworks=net9.0', '--no-restore', '-c', 'Release'])):
                if execute(argv, UPSTREAM, UPSTREAM / (operation + '.log')):
                    raise RuntimeError('Upstream ' + operation + ' failed; do not run mutations.')
            environment = dict(os.environ, DOTNET_ROLL_FORWARD='Major')
            code = execute(['dotnet', 'test', project, '-p:TargetFrameworks=net9.0', '--framework', 'net9.0', '--no-restore', '--no-build', '-c', 'Release', '--logger', 'trx', '--results-directory', 'results'], UPSTREAM, UPSTREAM / 'tests.log', environment)
            report['upstreamTestExitCode'] = code
            policy = json.loads((SAMPLE / '.csassay.json').read_text())
            policy['release']['tests'] = []
            policy['boundaries'] = {'coreProjects': ['src/CsvHelper/CsvHelper.csproj'], 'shellProjects': [], 'coreNamespaces': ['CsvHelper'], 'shellNamespaces': []}
            policy['representations'] = {'resultTypes': [], 'optionTypes': [], 'closedTypes': []}
            write(UPSTREAM / 'native-policy.json', policy)
            execute(['dotnet', ASSAY, 'check', UPSTREAM / 'src/CsvHelper/CsvHelper.csproj', '--policy', UPSTREAM / 'native-policy.json', '--json', UPSTREAM / 'native-check.json'], ROOT, UPSTREAM / 'native-check.log')
        if any(not (UPSTREAM / name).is_file() or sha(UPSTREAM / name) != value for name, value in source.items()):
            raise ValueError('Exported upstream source/test files differ from the pinned checkout.')
        reports = list((UPSTREAM / 'results').glob('*.trx'))
        if len(reports) != 1 or 'Build succeeded.' not in (UPSTREAM / 'build.log').read_text():
            raise ValueError('A single complete baseline build/test report is required.')
        report['tests'] = read_tests(reports[0])
        verification = json.loads((UPSTREAM / 'native-check.json').read_text())
        evidence = verification['evidence']
        report['nativeCheck'] = {'verdict': verification['verdict'], 'authoritative': evidence['authoritative'],
                                 'missingEvidenceCodes': sorted({x['code'] for x in evidence['missingEvidence']}),
                                 'failureCodes': sorted({x['code'] for x in evidence['failures']}),
                                 'receiptSha256': sha(UPSTREAM / 'native-check.json'),
                                 'scope': 'All original target frameworks, latest language rather than native preview; Framework reference packs missing.'}
        report['harness'] = {name: sha(UPSTREAM / name) for name in ('global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config', 'native-policy.json', 'restore.log', 'build.log', 'tests.log')}
        packet = {'schema': 'playground-mutation-experiment/0.1', 'synthetic': True,
                  'task': 'csvhelper-upstream-baseline-review',
                  'investigationContext': 'Original upstream tests have a recorded baseline failure; investigate portability and runtime context before mutation triage.',
                  'sourceSha256': hashlib.sha256(json.dumps(source, sort_keys=True).encode()).hexdigest(),
                  'baselinePassed': report['tests']['baselinePassed'], 'mutantCompiled': False,
                  'testsCompleted': True, 'survived': False, 'covered': None, 'hasBoundaryAssertion': None,
                  'upstreamTestReportSha256': report['tests']['reportSha256']}
        write(OUTPUT / 'upstream-baseline-experiment.json', packet)
        result = subprocess.run(['dotnet', str(ASSIST), str(OUTPUT / 'upstream-baseline-experiment.json')], cwd=ROOT, text=True, capture_output=True, timeout=40, check=True)
        report['decisionAssist'] = json.loads(result.stdout)
        if report['decisionAssist']['action'] != 'HumanReview' or report['decisionAssist']['verificationVerdict'] != 'Inconclusive' or report['decisionAssist']['provider'] != 'deterministic-baseline':
            raise ValueError('Upstream evidence must remain locally handled with advisory authority only.')
        write(OUTPUT / 'upstream-baseline-advice.json', report['decisionAssist'])
        report['executionStatus'] = 'completed-assessment'
        report['assessmentScriptSha256'] = sha(Path(__file__))
    except (OSError, ValueError, RuntimeError, KeyError, ET.ParseError, subprocess.SubprocessError) as failure:
        report['executionStatus'] = 'failed'
        report['failure'] = str(failure)
        write(OUTPUT / 'upstream-assessment.json', report)
        raise
    write(OUTPUT / 'upstream-assessment.json', report)
    print(json.dumps({k: report[k] for k in ('executionStatus', 'tests', 'nativeCheck', 'providerRequests')}, indent=2))


if __name__ == '__main__':
    main()
