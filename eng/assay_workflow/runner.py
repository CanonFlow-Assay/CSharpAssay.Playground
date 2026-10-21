"""Source-bound execution, intake and mutation preview shared by all consumers."""
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
import xml.etree.ElementTree as ET

TOOLKIT = Path(__file__).resolve().parents[2]
ENGINE = Path(os.environ.get('ASSAY_RUNNER', str(TOOLKIT.parent / 'CSharpAssay/src/CsAssay.Runner/bin/Release/net11.0/cs-assay.dll'))).resolve()
DECISION = TOOLKIT / 'src/Assay.Workflow.Cli/bin/Release/net11.0/Assay.Workflow.Cli.dll'
SDK = json.loads((TOOLKIT / 'global.json').read_text())['sdk']['version']
SUFFIXES = {'.cs', '.csproj', '.slnx', '.sln', '.json', '.props', '.targets', '.config', '.py', '.sh', '.xml'}
EXCLUDED = {'bin', 'obj', '.git', '.packages', '.upstream-cache', '.candidate-cache', '__pycache__', 'TestResults'}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()


def write(path, value):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_suffix(path.suffix + '.partial')
    pending.write_text(json.dumps(value, sort_keys=True, indent=2, ensure_ascii=False) + '\n')
    pending.replace(path)


def read(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError('Duplicate JSON property: ' + key)
            result[key] = value
        return result
    if Path(path).stat().st_size > 4 * 1024 * 1024: raise ValueError('Configuration exceeds the 4 MiB limit.')
    return json.loads(Path(path).read_text(), object_pairs_hook=unique)


def inside(root, value):
    path = (root / value).resolve()
    if not path.is_relative_to(root.resolve()): raise ValueError('Project path escapes its declared workspace: ' + str(value))
    return path


@dataclass(frozen=True)
class Execution:
    state: str
    exit_code: int
    output: str


def run(arguments, cwd, log, seconds=180):
    # Commands are selected by this runner; no shell or model-provided command text.
    process = subprocess.Popen([str(a) for a in arguments], cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, start_new_session=True)
    try:
        output, _ = process.communicate(timeout=seconds)
        state = 'Passed' if process.returncode == 0 else 'Failed'
    except (subprocess.TimeoutExpired, KeyboardInterrupt) as failure:
        os.killpg(process.pid, signal.SIGKILL)
        output, _ = process.communicate()
        state = 'Cancelled' if isinstance(failure, KeyboardInterrupt) else 'TimedOut'
    Path(log).parent.mkdir(parents=True, exist_ok=True); Path(log).write_text(output)
    if state == 'Cancelled': raise KeyboardInterrupt('Application execution cancelled; child process group stopped.')
    return Execution(state, process.returncode, output)


def require(execution, label):
    if execution.state != 'Passed': raise RuntimeError(label + ': ' + execution.state + '; inspect retained execution log.')


def load_manifest(path):
    path = Path(path).resolve(); value = read(path)
    if value.get('schema') != 'assay-project/0.1' or not re.fullmatch(r'[A-Za-z][A-Za-z0-9.-]{0,63}', value.get('id', '')):
        raise ValueError('Unsupported application manifest or unsafe project identity.')
    root = (path.parent / value['root']).resolve()
    for name in ('solution', 'policy', 'requirements', 'testProject', 'testDll', 'workerDll', 'contractBaseline', 'mutations'):
        inside(root, value[name])
    if not 1 <= value['minimumTests'] <= 100_000 or not 1 <= value['maximumMutations'] <= 12:
        raise ValueError('Invalid execution budget.')
    requirements = read(inside(root, value['requirements']))
    if requirements.get('schema') != 'assay-requirements/0.1' or not requirements.get('requirements'):
        raise ValueError('Explicit registered requirements are required.')
    ids = [r['id'] for r in requirements['requirements']]
    if len(ids) != len(set(ids)) or any(not r.get('description', '').strip() or not isinstance(r.get('probeIds'), list) for r in requirements['requirements']):
        raise ValueError('Duplicate or incomplete requirements.')
    for item in value['inputs']: inside(root, item)
    return root, value, requirements


def snapshot(root, manifest):
    paths = set()
    for name in manifest['inputs']:
        entry = inside(root, name)
        if not entry.exists(): raise ValueError('Missing declared input: ' + name)
        for path in ([entry] if entry.is_file() else entry.rglob('*')):
            if path.is_file() and (path.suffix in SUFFIXES or path.name == '.editorconfig') and not EXCLUDED.intersection(path.relative_to(root).parts):
                if not path.resolve().is_relative_to(root): raise ValueError('Source symlink escapes the workspace.')
                paths.add(path)
    files = {str(p.relative_to(root)): sha(p) for p in sorted(paths)}
    if not files: raise ValueError('Empty source snapshot.')
    return files, digest(files)


def toolkit_identity():
    paths = [ENGINE, DECISION, *Path(__file__).parent.glob('*.py')]
    paths += list(ENGINE.parent.glob('CsAssay.*.dll'))
    paths += list(DECISION.parent.glob('*.dll'))
    return {str(p): sha(p) for p in paths}


def intake(root, manifest, output):
    output.mkdir(parents=True, exist_ok=True)
    sdk = run(['dotnet', '--version'], root, output / 'sdk.log', 30)
    projects = []
    solution = inside(root, manifest['solution'])
    if solution.suffix == '.slnx':
        names = [p.attrib['Path'] for p in ET.parse(solution).getroot().iter('Project')]
    elif solution.suffix == '.csproj': names = [solution.name]
    else: raise ValueError('Preview intake supports explicit csproj or slnx inputs; classic solution conversion is not automatic.')
    for index, name in enumerate(names):
        project = inside(root, str(solution.parent.relative_to(root) / Path(name.replace('\\', '/'))))
        result = run(['dotnet', 'msbuild', project, '-nologo', '-getProperty:TargetFramework', '-getProperty:TargetFrameworks'], root, output / ('project-' + str(index) + '.log'), 60)
        try:
            properties = json.loads(result.output)['Properties']
            frameworks = [x for x in (properties.get('TargetFrameworks') or properties.get('TargetFramework', '')).split(';') if x]
        except (ValueError, KeyError): frameworks = []
        projects.append({'path': str(project.relative_to(root)), 'frameworks': frameworks, 'evaluated': result.state == 'Passed' and bool(frameworks)})
    support = 'Incomplete'
    if sdk.state == 'Passed' and sdk.output.strip() == SDK and projects and all(p['evaluated'] for p in projects):
        support = 'Native' if all(re.fullmatch(r'net11\.0(?:-.+)?', tfm) for p in projects for tfm in p['frameworks']) else 'Unsupported'
    return {'schema': 'assay-project-intake/0.1', 'support': support, 'sdk': sdk.output.strip(), 'requiredSdk': SDK, 'projects': projects,
            'scope': 'Evaluated declared projects; authoritative transitive graph/compiler completeness is determined by CSharpAssay. No automatic retargeting.'}


def test_record(path, execution, minimum, expected=None):
    if execution.state in ('TimedOut', 'Cancelled'): return {'state': execution.state, 'exitCode': execution.exit_code}
    if not Path(path).is_file(): return {'state': 'ToolFailure', 'reason': 'Missing xUnit execution artifact', 'exitCode': execution.exit_code}
    try:
        assemblies = ET.parse(path).getroot().findall('assembly')
        tests = [t for a in assemblies for t in a.findall('.//test')]
        counts = {k: sum(int(a.get(k, '0')) for a in assemblies) for k in ('total', 'passed', 'failed', 'skipped', 'errors', 'not-run')}
        if not assemblies or counts['total'] < minimum or counts['total'] != len(tests) or counts['total'] != counts['passed'] + counts['failed'] + counts['skipped'] or counts['skipped'] or counts['errors'] or counts['not-run'] or (expected is not None and counts['total'] != expected):
            raise ValueError('Incomplete or changed test selection.')
        if any(t.get('result') not in ('Pass', 'Fail') for t in tests): raise ValueError('Unknown test outcome.')
        if counts['passed'] != sum(t.get('result') == 'Pass' for t in tests) or counts['failed'] != sum(t.get('result') == 'Fail' for t in tests):
            raise ValueError('Summary contradicts individual test outcomes.')
        names = [t.get('name', '') for t in tests]
        if any(not n for n in names) or len(set(names)) != len(names): raise ValueError('Ambiguous test selection identity.')
        if (execution.exit_code == 0) != (counts['failed'] == 0): raise ValueError('Execution status contradicts test results.')
        return {'state': 'Failed' if counts['failed'] else 'Passed', **counts, 'testNames': sorted(names), 'reportSha256': sha(path), 'exitCode': execution.exit_code}
    except (ValueError, ET.ParseError): return {'state': 'ToolFailure', 'reason': 'Invalid or incomplete xUnit evidence', 'exitCode': execution.exit_code}


def tests(root, manifest, output, expected=None, coverage=False, expected_names=None):
    output.mkdir(parents=True, exist_ok=True); report = output / 'tests.xml'; report.unlink(missing_ok=True)
    args = ['dotnet', inside(root, manifest['testDll']), '--minimum-expected-tests', str(manifest['minimumTests']), '--report-xunit', '--report-xunit-filename', 'tests.xml', '--results-directory', output]
    if coverage:
        args += ['--coverage', '--coverage-output', output / 'coverage.cobertura.xml', '--coverage-output-format', 'cobertura']
    result = test_record(report, run(args, root, output / 'execution.log', 120), manifest['minimumTests'], expected)
    if expected_names is not None and result['state'] in ('Passed', 'Failed') and result.get('testNames') != expected_names:
        result = {'state': 'ToolFailure', 'reason': 'Mutation changed the named test selection.', 'execution': result}
    return result


def preview_mutations(root, manifest, output, baseline_total):
    corpus = read(inside(root, manifest['mutations'])); cases = corpus['cases']
    if corpus.get('schema') != 'assay-mutations/0.1' or not 1 <= len(cases) <= manifest['maximumMutations'] or len({c['id'] for c in cases}) != len(cases): raise ValueError('Invalid mutation corpus/budget.')
    output.mkdir(parents=True, exist_ok=True)
    records = []
    with tempfile.TemporaryDirectory(prefix='assay-application-') as directory:
        copy = Path(directory) / 'project'
        shutil.copytree(root, copy, ignore=lambda folder, names: [n for n in names if n in EXCLUDED or n == 'artifacts' or (n == 'generated' and Path(folder).name == 'evidence')])
        require(run(['dotnet', 'restore', inside(copy, manifest['solution']), '--locked-mode', '--configfile', root / 'NuGet.config'], copy, output / 'restore.log'), 'Isolated restore')
        require(run(['dotnet', 'build', inside(copy, manifest['solution']), '--no-restore', '-c', 'Release'], copy, output / 'baseline-build.log'), 'Isolated baseline build')
        isolated = tests(copy, manifest, output / 'original', baseline_total)
        if isolated['state'] != 'Passed': raise RuntimeError('Isolated original baseline did not pass.')
        for case in cases:
            if not re.fullmatch(r'[a-z0-9-]{1,64}', case['id']): raise ValueError('Unsafe mutation identity.')
            target = inside(copy, case['path'])
            if target.suffix != '.cs' or not target.is_relative_to(copy / 'src') and not target.is_relative_to(copy / 'samples/70-polly-resilience/src'):
                raise ValueError('Mutation must target declared production source.')
            original = target.read_bytes(); text = original.decode()
            if text.count(case['before']) != 1 or not case['before'] or not case['requirementId'] in {r['id'] for r in read(inside(copy, manifest['requirements']))['requirements']}:
                raise ValueError('Mutation span or requirement is ambiguous.')
            folder = output / case['id']; folder.mkdir(exist_ok=True)
            try:
                target.write_text(text.replace(case['before'], case['after'], 1))
                source_hash = sha(target)
                build = run(['dotnet', 'build', inside(copy, manifest['solution']), '--no-restore', '-c', 'Release'], copy, folder / 'build.log')
                if build.state in ('TimedOut', 'Cancelled'): state = build.state; execution = {'state': state}
                elif build.state != 'Passed': state = 'Invalid'; execution = {'state': state}
                else:
                    execution = tests(copy, manifest, folder, baseline_total, expected_names=isolated['testNames'])
                    state = {'Passed': 'Survived', 'Failed': 'Killed'}.get(execution['state'], execution['state'])
                row = {'id': case['id'], 'path': case['path'], 'requirementId': case['requirementId'], 'state': state, 'execution': execution,
                       'mutatedSourceSha256': source_hash, 'baselineSourceSha256': hashlib.sha256(original).hexdigest(),
                       'buildLogSha256': sha(folder / 'build.log'), 'expectedState': case['expectedState'], 'excludedAsEquivalent': False}
                records.append(row); write(output / 'mutations.json', {'schema': 'assay-mutation-preview/0.1', 'executionStatus': 'running', 'results': records})
            finally: target.write_bytes(original)
    report = {'schema': 'assay-mutation-preview/0.1', 'executionStatus': 'completed', 'results': records,
              'unexpectedOutcomes': [r['id'] for r in records if r['state'] != r['expectedState']],
              'authority': 'Bounded declared edits, preview only; no automatic equivalence exclusion or universal mutation score.'}
    write(output / 'mutations.json', report)
    return report
