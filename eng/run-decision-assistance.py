#!/usr/bin/env python3
"""Execute the bounded, agent-reviewed mutation corpus offline; never call models."""
from pathlib import Path
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/50-decision-assistance'
OUTPUT = ROOT / 'evidence/generated/decision-assistance'
MANIFEST = SAMPLE / 'corpus/mutation-cases.json'
PIN = json.loads((ROOT / 'global.json').read_text())['sdk']['version']
BUILD_INPUTS = ['global.json', 'Directory.Build.props', 'Directory.Build.targets', '.editorconfig']
ASSEMBLY = 'bin/Release/net11.0/AmountProbe.dll'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def command(argv, cwd, environment=None):
    return subprocess.run(argv, cwd=cwd, capture_output=True, text=True,
                          timeout=120, env=environment)


def require_success(result, operation):
    if result.returncode:
        raise RuntimeError(f'{operation} failed with exit code {result.returncode}.')
    return result.stdout.strip()


def compilation(result):
    # A restore outage, crash, or unrelated compiler error is never an expected mutant.
    errors = sorted(set(re.findall(r'\berror (\w+):', result.stdout + result.stderr)))
    if result.returncode == 0 and not errors:
        return {'status': 'compiled', 'exitCode': 0, 'diagnostics': []}
    if result.returncode == 1 and errors and all(re.fullmatch(r'CS\d+', code) for code in errors):
        return {'status': 'invalid', 'exitCode': 1, 'diagnostics': errors}
    raise RuntimeError('Unexpected build failure: ' + (', '.join(errors) or 'no identified diagnostics') + '.')


def compile_probe(target, source):
    target.mkdir()
    for name in BUILD_INPUTS:
        shutil.copyfile(ROOT / name, target / name)
    shutil.copyfile(SAMPLE / 'probe/AmountProbe.csproj', target / 'AmountProbe.csproj')
    shutil.copyfile(SAMPLE / 'probe/Program.cs', target / 'Program.cs')
    (target / 'Amount.cs').write_bytes(source)
    (target / 'NuGet.config').write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
    require_success(command(['dotnet', 'restore', 'AmountProbe.csproj', '--configfile', 'NuGet.config'], target), 'Isolated restore')
    return compilation(command(['dotnet', 'build', 'AmountProbe.csproj', '--no-restore', '-c', 'Release'], target))


def probe_observation(result, suite):
    if result.returncode not in (0, 7):
        raise RuntimeError('Probe crashed or did not complete with its declared test exit code.')
    observation = json.loads(result.stdout)
    if not isinstance(observation, dict) or set(observation) != {'tests', 'failures', 'policyCalls'}:
        raise ValueError('Unexpected probe record.')
    if any(type(value) is not int for value in observation.values()):
        raise ValueError('Probe counts must be integers.')
    if (observation['tests'] != suite['tests'] or observation['policyCalls'] != suite['policyCalls'] or
            not 0 <= observation['failures'] <= observation['tests'] or
            (result.returncode == 0) != (observation['failures'] == 0)):
        raise ValueError('Probe counts or exit status disagree with the declared execution contract.')
    return {**observation, 'exitCode': result.returncode}


def observe(target, suite_name, suites):
    return probe_observation(command(['dotnet', ASSEMBLY, suite_name], target), suites[suite_name])


def execute_case(case, suites, original_target, scratch, original):
    selected = case['selectedSuite']
    baseline = observe(original_target, selected, suites)
    if baseline['failures'] != case['expectedBaselineFailures']:
        raise RuntimeError('Original baseline failure count disagrees with the case requirement.')
    record = {
        'baselinePassed': baseline['failures'] == 0, 'mutantCompiled': False,
        'testsCompleted': False, 'survived': False, 'covered': None,
        'hasBoundaryAssertion': suites[selected]['hasBoundaryAssertion'],
        'observations': {'original-selected': baseline},
        'mutantBuild': {'status': 'not_run_baseline_failed'},
        'repairDemonstrated': False, 'outcome': 'baseline_failed',
        'survivorDisposition': 'not_applicable',
        'probeCommands': [['dotnet', ASSEMBLY, selected]],
    }
    if not record['baselinePassed']:
        return record
    mutant_target = scratch / case['id']
    mutant = original.replace(b'amount > 0', case['replacement'].encode())
    build = compile_probe(mutant_target, mutant)
    record['mutantBuild'] = build
    if build['status'] == 'invalid':
        if (case.get('expectedDiagnostic') not in build['diagnostics'] or
                build['diagnostics'] != [case['expectedDiagnostic']]):
            raise RuntimeError('Mutant failed with unexpected diagnostics.')
        record['outcome'] = 'invalid'
        return record
    record['mutantCompiled'] = True
    observed = observe(mutant_target, selected, suites)
    if observed['failures'] != case['expectedSelectedFailures']:
        raise RuntimeError('Mutant assertion failure count disagrees with the reviewed expectation.')
    record['observations']['mutant-selected'] = observed
    record.update(testsCompleted=True, survived=observed['failures'] == 0,
                  covered=observed['policyCalls'] > 0,
                  outcome='survived' if observed['failures'] == 0 else 'killed')
    if record['survived']:
        record['survivorDisposition'] = 'unresolved'
    if 'probeSuite' in case:
        suite = case['probeSuite']
        base_probe = observe(original_target, suite, suites)
        mutant_probe = observe(mutant_target, suite, suites)
        if base_probe['failures'] or mutant_probe['failures'] != case['expectedProbeFailures']:
            raise RuntimeError('Additional probe did not demonstrate the declared original/mutant behavior.')
        record['observations'].update({'original-probe': base_probe, 'mutant-probe': mutant_probe})
        record['probeCommands'].append(['dotnet', ASSEMBLY, suite])
        record['repairDemonstrated'] = record['survived'] and mutant_probe['failures'] > 0
    return record


def advice_for(path, *options, environment=None):
    cli = SAMPLE / 'src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'
    result = command(['dotnet', str(cli), str(path), *options], ROOT, environment)
    advice = json.loads(require_success(result, 'Advice CLI'))
    if advice['schema'] != 'playground-decision-advice/0.1' or advice['verificationVerdict'] != 'Inconclusive':
        raise RuntimeError('Advice changed the experiment authority boundary.')
    return advice


def write_json(path, value):
    path.write_text(json.dumps(value, sort_keys=True, indent=2) + '\n')


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report_path = OUTPUT / 'mutation-cases.json'
    report = {'schema': 'playground-mutation-case-results/0.1', 'executionStatus': 'running',
              'synthetic': True, 'executed': True, 'hypotheticalStates': False, 'results': [],
              'authority': 'Bounded fixture executions; not a model benchmark or Assay verification receipt.'}
    write_json(report_path, report)
    current_case = None
    try:
        if require_success(command(['dotnet', '--version'], ROOT), 'SDK check') != PIN:
            raise RuntimeError('The exact repository SDK is required.')
        corpus = json.loads(MANIFEST.read_text())
        if corpus['schema'] != 'playground-executed-mutation-cases/0.1' or corpus['synthetic'] is not True:
            raise ValueError('Unsupported executed corpus.')
        ids = [case['id'] for case in corpus['cases']]
        if not ids or len(ids) != len(set(ids)) or any(not re.fullmatch(r'[a-z0-9-]+', name) for name in ids):
            raise ValueError('Case identifiers must be unique safe directory names.')
        original = (SAMPLE / 'probe/Amount.cs').read_bytes()
        if original.count(b'amount > 0') != 1:
            raise RuntimeError('Expected exactly one mutation site; review the corpus.')
        identity_files = {
            'sourceSha256': SAMPLE / 'probe/Amount.cs', 'probeSha256': SAMPLE / 'probe/Program.cs',
            'projectSha256': SAMPLE / 'probe/AmountProbe.csproj', 'manifestSha256': MANIFEST,
            'harnessSha256': Path(__file__),
            'cliSha256': SAMPLE / 'src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll',
        }
        identities = {'sdk': PIN, **{name: digest(path.read_bytes()) for name, path in identity_files.items()},
                      'buildInputSha256': {name: digest((ROOT / name).read_bytes()) for name in BUILD_INPUTS}}
        report.update(**identities, reviewStatus=corpus['reviewStatus'], requirements=corpus['requirements'])
        with tempfile.TemporaryDirectory(prefix='csassay-decision-') as temporary:
            scratch = Path(temporary)
            original_target = scratch / 'original'
            original_build = compile_probe(original_target, original)
            if original_build['status'] != 'compiled':
                raise RuntimeError('Original fixture did not compile.')
            report['originalBuild'] = original_build
            for case in corpus['cases']:
                current_case = case['id']
                receipt = {'schema': 'playground-mutation-experiment/0.1', 'synthetic': True,
                           **identities, 'caseId': current_case, 'mutation': current_case + '/0.1',
                           'selectedSuite': case['selectedSuite'],
                           'originalBuild': original_build,
                           'mutantSourceSha256': digest(original.replace(b'amount > 0', case['replacement'].encode())),
                           'reviewStatus': corpus['reviewStatus'], 'review': case['review'],
                           'coverageBasis': 'Probe direct-call counter only; not instrumented source/branch coverage.',
                           'assertionBasis': 'Declared selected-suite fixture assertions, bound by probe source hash.',
                           'authority': report['authority'],
                           **execute_case(case, corpus['suites'], original_target, scratch, original)}
                if receipt['outcome'] != case['expectedOutcome']:
                    raise RuntimeError('Observed mutation outcome disagrees with the reviewed case expectation.')
                folder = OUTPUT / 'cases' / current_case
                folder.mkdir(parents=True, exist_ok=True)
                path = folder / 'experiment.json'
                write_json(path, receipt)
                advice = advice_for(path)
                if advice['action'] != case['expectedAction'] or advice['adviceStatus'] != case['expectedStatus']:
                    raise RuntimeError('Observed advice disagrees with the case review.')
                write_json(folder / 'advice.json', advice)
                report['results'].append({'id': current_case, 'outcome': receipt['outcome'],
                                          'action': advice['action'], 'adviceStatus': advice['adviceStatus'],
                                          'verificationVerdict': advice['verificationVerdict'],
                                          'repairDemonstrated': receipt['repairDemonstrated'],
                                          'survivorDisposition': receipt['survivorDisposition'],
                                          'review': case['review'], 'expectationsMatched': True,
                                          'experimentSha256': digest(path.read_bytes()),
                                          'adviceSha256': digest((folder / 'advice.json').read_bytes()),
                                          'experiment': str(path.relative_to(OUTPUT))})
                if current_case == 'zero-boundary-gap':
                    # Keep existing entry points for the demonstration and hypothetical evaluation.
                    write_json(OUTPUT / 'experiment.json', receipt)
                    write_json(OUTPUT / 'advice.json', advice)
                    firm = advice_for(path, '--firm')
                    if firm['action'] != advice['action'] or not firm['message'].startswith('Attention:'):
                        raise RuntimeError('Feedback tone changed the investigation.')
                    write_json(OUTPUT / 'firm-advice.json', firm)
                    no_key = dict(os.environ)
                    no_key.pop('TYPESAFE_API_KEY', None)
                    unavailable = advice_for(path, '--jev', environment=no_key)
                    if unavailable['adviceStatus'] != 'unavailable' or unavailable['deterministicAction'] != advice['action']:
                        raise RuntimeError('Missing optional provider hid the deterministic advice.')
                    write_json(OUTPUT / 'unavailable-advice.json', unavailable)
        if (any(digest(path.read_bytes()) != identities[name] for name, path in identity_files.items()) or
                any(digest((ROOT / name).read_bytes()) != identities['buildInputSha256'][name] for name in BUILD_INPUTS)):
            raise RuntimeError('A bound execution input changed during the run.')
        report['executionStatus'] = 'completed'
        report['summary'] = {outcome: sum(row['outcome'] == outcome for row in report['results'])
                             for outcome in ('survived', 'killed', 'invalid', 'baseline_failed')}
    except (RuntimeError, ValueError, OSError, KeyError, TypeError, subprocess.TimeoutExpired) as failure:
        report.update(executionStatus='failed', failedCase=current_case,
                      failureType=type(failure).__name__, failureMessage=str(failure)[:300])
        write_json(report_path, report)
        print('Mutation corpus incomplete; inspect', report_path.relative_to(ROOT), 'and rerun after resolving the failure.')
        raise
    write_json(report_path, report)
    print(json.dumps({'executionStatus': 'completed', 'cases': len(report['results']),
                      'outcomes': report['summary'], 'report': str(report_path.relative_to(ROOT))}))


if __name__ == '__main__':
    main()
