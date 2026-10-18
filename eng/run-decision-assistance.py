#!/usr/bin/env python3
"""Run one isolated source mutation and its declared behavior probes, offline."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / 'samples/50-decision-assistance'
OUTPUT = ROOT / 'evidence/generated/decision-assistance'
PIN = json.loads((ROOT / 'global.json').read_text())['sdk']['version']


def run(argv, cwd, expected=0, environment=None):
    result = subprocess.run(argv, cwd=cwd, capture_output=True, text=True, timeout=120, env=environment)
    if result.returncode != expected:
        raise RuntimeError(f'{argv[0]} returned {result.returncode}, expected {expected}\n{result.stdout[-4000:]}\n{result.stderr[-1000:]}')
    return result.stdout.strip()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    if run(['dotnet', '--version'], ROOT) != PIN:
        raise RuntimeError('The exact repository SDK is required.')
    original = (SAMPLE / 'probe/Amount.cs').read_bytes()
    probe = (SAMPLE / 'probe/Program.cs').read_bytes()
    needle = b'amount > 0'
    if original.count(needle) != 1:
        raise RuntimeError('The expected single mutation site changed; review the experiment.')
    mutant = original.replace(needle, b'amount >= 0')
    results = {}
    with tempfile.TemporaryDirectory(prefix='csassay-decision-') as temporary:
        scratch = Path(temporary)
        for version, source in [('original', original), ('mutant', mutant)]:
            target = scratch / version
            target.mkdir()
            for name in ['global.json', 'Directory.Build.props', 'Directory.Build.targets', '.editorconfig']:
                shutil.copyfile(ROOT / name, target / name)
            shutil.copyfile(SAMPLE / 'probe/AmountProbe.csproj', target / 'AmountProbe.csproj')
            (target / 'Amount.cs').write_bytes(source)
            (target / 'Program.cs').write_bytes(probe)
            (target / 'NuGet.config').write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
            run(['dotnet', 'restore', 'AmountProbe.csproj', '--configfile', 'NuGet.config'], target)
            run(['dotnet', 'build', 'AmountProbe.csproj', '--no-restore', '-c', 'Release'], target)
            assembly = 'bin/Release/net11.0/AmountProbe.dll'
            for suite in ['weak', 'strong']:
                expected = 7 if version == 'mutant' and suite == 'strong' else 0
                observation = json.loads(run(['dotnet', str(assembly), suite], target, expected))
                expected_tests = 2 if suite == 'weak' else 3
                if observation != {'tests': expected_tests, 'failures': 1 if expected else 0}:
                    raise RuntimeError('Unexpected probe evidence; the experiment is incomplete.')
                results[version + '-' + suite] = observation
    if original != (SAMPLE / 'probe/Amount.cs').read_bytes() or probe != (SAMPLE / 'probe/Program.cs').read_bytes():
        raise RuntimeError('Tracked experiment source changed during execution.')
    receipt = {
        'schema': 'playground-mutation-experiment/0.1',
        'synthetic': True,
        'sdk': PIN,
        'mutation': 'amount-positive-boundary/0.1',
        'sourceSha256': digest(original),
        'mutantSourceSha256': digest(mutant),
        'probeSha256': digest(probe),
        'buildInputSha256': {name: digest((ROOT / name).read_bytes()) for name in ['global.json', 'Directory.Build.props', 'Directory.Build.targets', '.editorconfig']},
        'projectSha256': digest((SAMPLE / 'probe/AmountProbe.csproj').read_bytes()),
        'probeCommands': [['dotnet', 'bin/Release/net11.0/AmountProbe.dll', suite] for suite in ['weak', 'strong']],
        'selectedSuite': 'weak',
        'baselinePassed': True,
        'mutantCompiled': True,
        'testsCompleted': True,
        'survived': True,
        'covered': None,
        'hasBoundaryAssertion': False,
        'observations': results,
        'repairDemonstrated': True,
        'authority': 'Bounded experiment only; not an Assay verification receipt.',
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    path = OUTPUT / 'experiment.json'
    path.write_text(json.dumps(receipt, sort_keys=True, indent=2) + '\n')
    cli = SAMPLE / 'src/DecisionAssist.Cli/bin/Release/net11.0/DecisionAssist.Cli.dll'
    advice = json.loads(run(['dotnet', str(cli), str(path)], ROOT))
    if advice['action'] != 'InspectAssertion' or advice['verificationVerdict'] != 'Inconclusive':
        raise RuntimeError('Advice changed its authority boundary or lost the expected investigation.')
    firm = json.loads(run(['dotnet', str(cli), str(path), '--firm'], ROOT))
    if firm['action'] != advice['action'] or firm['verificationVerdict'] != advice['verificationVerdict'] or not firm['message'].startswith('Attention:'):
        raise RuntimeError('Feedback tone changed action or authority.')
    no_key = dict(os.environ)
    no_key.pop('TYPESAFE_API_KEY', None)
    unavailable = json.loads(run(['dotnet', str(cli), str(path), '--jev'], ROOT, environment=no_key))
    if unavailable['adviceStatus'] != 'unavailable' or unavailable['deterministicAction'] != 'InspectAssertion' or unavailable['verificationVerdict'] != 'Inconclusive':
        raise RuntimeError('Missing optional provider hid baseline advice or changed authority.')
    (OUTPUT / 'unavailable-advice.json').write_text(json.dumps(unavailable, sort_keys=True, indent=2) + '\n')
    (OUTPUT / 'firm-advice.json').write_text(json.dumps(firm, sort_keys=True, indent=2) + '\n')
    (OUTPUT / 'advice.json').write_text(json.dumps(advice, sort_keys=True, indent=2) + '\n')
    print('Original: weak and strong probes passed. Mutant: weak survived; zero-boundary test killed it.')
    print(advice['message'])
    print('Experiment and advisory records:', OUTPUT.relative_to(ROOT))


if __name__ == '__main__':
    main()
