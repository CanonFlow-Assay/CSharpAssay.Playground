#!/usr/bin/env python3
"""Execute the bounded Polly, packaged starter and requirement-change qualification."""
import argparse
from collections import Counter
from assay_workflow.application import *
from assay_workflow.scaffold import create


def ready(report, total):
    if report['executionStatus'] != 'completed' or report['workflowStatus'] != 'ReadyForReview':
        raise ValueError('Application did not reach review readiness.')
    modules = {m['module']: m for m in report['assessment']['modules']}
    if any(modules[m]['state'] != 'Passed' for m in ('Behavior', 'CSharpAssay', 'ContractAssay', 'ResilienceAssay')):
        raise ValueError('A required module is not passing.')
    return {'tests': total, 'workflowStatus': report['workflowStatus'], 'decision': report['decision']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=TOOLKIT / 'evidence/generated/application-workflow')
    args = parser.parse_args(); output = args.output.resolve(); output.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'assay-application-qualification/0.1', 'executionStatus': 'running', 'providerRequests': 0}
    write(output / 'qualification.json', report)
    try:
        require(run(['python3', TOOLKIT / 'eng/test-application-workflow.py'], TOOLKIT, output / 'python-tests.log'), 'Workflow boundary tests')
        print('Qualifying real Polly consumer and six registered mutations.', flush=True)
        polly = verify(TOOLKIT / 'samples/70-polly-resilience/assay.app.json', output / 'polly')
        report['polly'] = ready(polly, read(output / 'polly/behavior.json')['total'])
        report['polly']['mutations'] = dict(Counter(r['state'] for r in read(output / 'polly/mutation.json')['results']))
        # Unique, retained application roots make reruns non-destructive.
        app = Path(tempfile.mkdtemp(prefix='undertaking-', dir=output)) / 'ReadyApp'
        print('Generating independent package-consuming application: ' + str(app), flush=True)
        create(app, 'ReadyApp'); manifest_path = app / 'assay.app.json'
        original = verify(manifest_path, output / 'new-app-baseline')
        report['newApplication'] = {'path': str(app), **ready(original, read(output / 'new-app-baseline/behavior.json')['total'])}
        request = {'schema': 'assay-change-request/0.1', 'description': 'Treat successful transport replies with blank content as permanent failure.',
                   'acceptanceCriteria': ['Empty and whitespace-only content produce FetchFailed(Permanent).', 'Nonempty successful content is preserved.', 'Existing public response contract and previous tests remain compatible.']}
        request_path = output / 'change-request.json'; write(request_path, request)
        plan(manifest_path, request_path, output / 'change/work-item.json')
        requirements = read(app / 'requirements.json')
        requirements['requirements'].append({'id': 'nonblank-content', 'description': request['description'], 'probeIds': ['behavior']})
        write(app / 'requirements.json', requirements)
        test_path = app / 'tests/ReadyApp.Tests/BlankContentTests.cs'
        test_path.write_text('''using ReadyApp.Domain;
using Xunit;
namespace ReadyApp.Tests;
public sealed class BlankContentTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankSuccessfulContentIsPermanentFailure(string content)
    {
        var outcome = FetchDecisions.Convert(new(ReplyKind.Success, content));
        Assert.Equal(new FetchFailed(FetchFailureKind.Permanent), Assert.IsType<FetchFailed>(outcome.Value));
    }
}
''')
        manifest = read(manifest_path); manifest['minimumTests'] = 16; write(manifest_path, manifest)
        policy = read(app / '.csassay.json'); policy['release']['tests'][0]['minimumExpectedTests'] = 16; write(app / '.csassay.json', policy)
        require(run(['dotnet', 'format', 'whitespace', app / 'App.slnx', '--no-restore'], app, output / 'change/format.log'), 'Requirement test formatting')
        require(run(['dotnet', 'build', app / 'App.slnx', '--no-restore', '-c', 'Release'], app, output / 'change/red-build.log'), 'Requirement test build')
        red = tests(app, manifest, output / 'change/red', 16)
        if red['state'] != 'Failed' or red.get('failed') != 2: raise ValueError('New requirements did not produce the two expected failing tests.')
        red['inputSha256'], red['snapshotSha256'] = snapshot(app, manifest)
        write(output / 'change/red.json', red)
        source = app / 'src/ReadyApp.Domain/FetchDecisions.cs'; text = source.read_text()
        old = '        ReplyKind.Success => new Fetched(reply.Content),'
        if text.count(old) != 1: raise ValueError('Requirement implementation span changed.')
        source.write_text(text.replace(old, '        ReplyKind.Success when string.IsNullOrWhiteSpace(reply.Content) => new FetchFailed(FetchFailureKind.Permanent),\n' + old))
        require(run(['dotnet', 'format', 'whitespace', app / 'App.slnx', '--no-restore'], app, output / 'change/implementation-format.log'), 'Requirement implementation formatting')
        print('Reassessing implemented requirement against preserved baseline.', flush=True)
        candidate = verify(manifest_path, output / 'new-app-candidate', output / 'new-app-baseline/run.json')
        report['change'] = {**ready(candidate, read(output / 'new-app-candidate/behavior.json')['total']), 'redTests': red['failed'],
                            'previousTestsRetained': True, 'contractPinRetained': original['pinnedContractSha256'] == candidate['pinnedContractSha256'],
                            'mutations': dict(Counter(r['state'] for r in read(output / 'new-app-candidate/mutation.json')['results']))}
        if not report['change']['contractPinRetained']: raise ValueError('Candidate changed its pinned contract.')
        legacy = output / 'legacy-intake'; legacy.mkdir(exist_ok=True)
        shutil.copyfile(TOOLKIT / 'global.json', legacy / 'global.json')
        project = legacy / 'Legacy.csproj'; project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n')
        before = sha(project); observed = intake(legacy, {'solution': 'Legacy.csproj'}, legacy / 'execution')
        if observed['support'] != 'Unsupported' or before != sha(project): raise ValueError('Legacy intake did not preserve unsupported scope.')
        write(legacy / 'intake.json', observed); report['legacyIntake'] = 'Unsupported; source unchanged'
        report.update(executionStatus='completed', scope='Bounded document-fetch consumer and its requirement-change loop; preview modules do not grant release approval.',
                      remainingEvidence=['Independent human review', 'Live provider quality and held-out evaluation', 'Engine analyzer performance budget is a separate required check; this workflow does not waive it.'],
                      evidenceSha256={str(p.relative_to(output)): sha(p) for p in [output / 'polly/run.json', output / 'new-app-baseline/run.json', output / 'new-app-candidate/run.json', output / 'change/red.json', legacy / 'intake.json']})
        write(output / 'qualification.json', report); print(json.dumps(report, indent=2)); return 0
    except BaseException as failure:
        report.update(executionStatus='failed', failure=str(failure)); write(output / 'qualification.json', report); raise


if __name__ == '__main__':
    raise SystemExit(main())
