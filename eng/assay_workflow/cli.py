"""DAA developer entry point: new, intake, verify, plan and registered investigations."""
import argparse
import fcntl
from .application import *
from .scaffold import create


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    new = commands.add_parser('new'); new.add_argument('--output', required=True, type=Path); new.add_argument('--name', required=True)
    for command in ('intake', 'verify', 'plan', 'investigate'):
        p = commands.add_parser(command); p.add_argument('--manifest', required=True, type=Path); p.add_argument('--output', type=Path)
        if command == 'verify': p.add_argument('--baseline', type=Path); p.add_argument('--skip-mutations', action='store_true')
        if command == 'plan': p.add_argument('--requirement', required=True, type=Path)
        if command == 'investigate': p.add_argument('--run', required=True, type=Path); p.add_argument('--probe', required=True, choices=('behavior', 'contract', 'resilience'))
    args = parser.parse_args()
    try:
        if args.command == 'new': result = create(args.output, args.name)
        else:
            root, manifest, requirements = load_manifest(args.manifest)
            output = (args.output or root / 'evidence/generated/application-assessment').resolve()
            if args.command == 'intake':
                result = intake(root, manifest, output / 'intake'); write(output / 'intake.json', result)
            elif args.command == 'verify':
                result = verify(args.manifest, output, args.baseline, not args.skip_mutations)
                print(json.dumps({'executionStatus': result['executionStatus'], 'workflowStatus': result['workflowStatus'], 'receipt': str(output / 'run.json'), 'decision': result['decision']}, indent=2))
                return 0 if result['workflowStatus'] == 'ReadyForReview' else 2
            elif args.command == 'plan': result = plan(args.manifest, args.requirement, output / 'work-item.json')
            else:
                previous_path = args.run.resolve(); previous = read(previous_path)
                if previous.get('executionStatus') != 'completed': raise ValueError('Investigation requires completed original evidence.')
                _, identity = snapshot(root, manifest)
                if identity != previous['snapshotSha256']: raise ValueError('The decision is stale after a source/configuration change; reassess first.')
                parent = previous_path.parent
                validated = decision(previous['assessment'], output / 'selection', args.probe, identity)
                if validated['decision']['action'] != 'RunRegisteredProbe' or validated['decision']['probeId'] != args.probe:
                    raise ValueError('That investigation is not eligible for the observed assessment.')
                budget = parent / 'probe-budget.json'
                with budget.open('r+') as stream:
                    fcntl.flock(stream.fileno(), fcntl.LOCK_EX)
                    remaining = json.load(stream)
                    if remaining['snapshotSha256'] != identity or remaining['remaining'] <= 0: raise ValueError('Registered investigation budget exhausted or stale.')
                    remaining['remaining'] -= 1; stream.seek(0); json.dump(remaining, stream); stream.truncate()
                if args.probe == 'behavior': result = tests(root, manifest, output / 'probe')
                else:
                    execution = run(['dotnet', inside(root, manifest['workerDll'])], root, output / 'probe.log', 60)
                    require(execution, 'Registered controlled consumer probe'); result = json.loads(execution.output)
                write(output / 'probe-result.json', result)
        print(json.dumps(result, indent=2)); return 0
    except (OSError, ValueError, RuntimeError, KeyError, ET.ParseError) as failure:
        print(json.dumps({'executionStatus': 'failed', 'failure': str(failure)})); return 2


if __name__ == '__main__':
    raise SystemExit(main())
