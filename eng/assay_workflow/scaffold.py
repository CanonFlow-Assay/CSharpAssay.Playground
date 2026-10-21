"""Create a standalone pinned starter, using packages rather than engine source references."""
from .runner import *


def create(destination, name):
    if not re.fullmatch('[A-Za-z][A-Za-z0-9]{0,47}', name): raise ValueError('Choose a simple C# application identity.')
    destination = Path(destination).resolve()
    if destination.exists(): raise ValueError('Scaffolding never overwrites an existing destination.')
    source = TOOLKIT / 'samples/70-polly-resilience'
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix='.assay-new-', dir=destination.parent))
    try:
        for filename in ['global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', '.editorconfig']:
            shutil.copyfile(TOOLKIT / filename, temporary / filename)
        # Keep the standalone project independent of the toolkit's source layout.
        for project in ('Domain', 'Adapter'):
            target = temporary / 'src' / (name + '.' + project); target.mkdir(parents=True)
            for path in (source / 'src' / ('PollyAssay.' + project)).glob('*.cs'):
                (target / path.name).write_text(path.read_text().replace('PollyAssay', name))
        worker = temporary / 'worker'; worker.mkdir()
        (worker / 'Program.cs').write_text((source / 'worker/Program.cs').read_text().replace('PollyAssay', name))
        tests_dir = temporary / 'tests' / (name + '.Tests'); tests_dir.mkdir(parents=True)
        (tests_dir / 'BehaviorTests.cs').write_text((TOOLKIT / 'tests/Assay.Workflow.Tests/PollyTests.cs').read_text().replace('PollyAssay', name).replace('Assay.Workflow.Tests', name + '.Tests'))
        (tests_dir / 'WorkflowApiTests.cs').write_text('''using Assay.Workflow.Domain;
using Playground.DecisionAssist.Domain;
using Xunit;
namespace ''' + name + '''.Tests;
public sealed class WorkflowApiTests
{
    [Fact]
    public void UnsupportedApplicationScopeRequestsContextThroughPackagedApi()
    {
        var assessment = new Assessment(AssessmentRules.Schema, new string('a', 64), new string('b', 64), SupportStatus.Unsupported, ExecutionState.NotRun,
            [new("fetch", "Declared requirement", [])], []);
        Assert.IsType<ContextRequired>(ApplicationAssistance.Decide(assessment, [], 1).Value);
    }
}
''')
        (temporary / 'tests/Directory.Build.props').write_text((TOOLKIT / 'tests/Directory.Build.props').read_text())
        files = {
            f'src/{name}.Domain/{name}.Domain.csproj': '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="CsAssay.Analyzers" PrivateAssets="all" /></ItemGroup></Project>',
            f'src/{name}.Adapter/{name}.Adapter.csproj': f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../{name}.Domain/{name}.Domain.csproj" /><PackageReference Include="CanonFlow.Assay.Workflow" VersionOverride="0.1.0-preview.1" /><PackageReference Include="Polly.Core" VersionOverride="8.6.5" /><PackageReference Include="Polly.RateLimiting" VersionOverride="8.6.5" /><PackageReference Include="CsAssay.Analyzers" PrivateAssets="all" /></ItemGroup></Project>',
            'worker/Worker.csproj': f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><ProjectReference Include="../src/{name}.Adapter/{name}.Adapter.csproj" /></ItemGroup></Project>',
            f'tests/{name}.Tests/{name}.Tests.csproj': f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../../src/{name}.Adapter/{name}.Adapter.csproj" /><PackageReference Include="CanonFlow.DecisionAssist" VersionOverride="0.1.0-preview.1" /><PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" /></ItemGroup></Project>'}
        for filename, text in files.items(): (temporary / filename).write_text(text + '\n')
        (temporary / 'App.slnx').write_text('<Solution>\n' + ''.join(f'  <Project Path="{p}" />\n' for p in files) + '</Solution>\n')
        import xml.sax.saxutils as xml_utils
        feed = xml_utils.escape(str(TOOLKIT / '.packages'), {'"': '&quot;'})
        (temporary / 'NuGet.config').write_text(f'<configuration><config><add key="globalPackagesFolder" value=".packages/cache"/></config><packageSources><clear/><add key="assay-local" value="{feed}"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="assay-local"><package pattern="CsAssay.*"/><package pattern="CanonFlow.*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>\n')
        for filename in ('requirements.json', 'contract.json'):
            shutil.copyfile(source / filename, temporary / filename)
        shutil.copytree(source / 'licenses', temporary / 'licenses')
        shutil.copyfile(source / 'upstream.json', temporary / 'upstream.json')
        corpus = read(source / 'mutations.json')
        for case in corpus['cases']:
            case['path'] = case['path'].replace('samples/70-polly-resilience/', '').replace('PollyAssay', name)
        write(temporary / 'mutations.json', corpus)
        policy = read(TOOLKIT / '.csassay.workflow.json')
        policy['release']['tests'] = [{'input': f'tests/{name}.Tests/{name}.Tests.csproj', 'configuration': 'Release', 'noBuild': True, 'minimumExpectedTests': 14}]
        policy['boundaries'] = {'coreProjects': [f'src/{name}.Domain/{name}.Domain.csproj'], 'shellProjects': [p for p in files if '.Domain/' not in p], 'coreNamespaces': [name + '.Domain'], 'shellNamespaces': [name + '.Adapter', name + '.Tests']}
        policy['representations']['closedTypes'] = [name + '.Domain.FetchOutcome']
        write(temporary / '.csassay.json', policy)
        manifest = {'schema': 'assay-project/0.1', 'id': name, 'root': '.', 'solution': 'App.slnx', 'policy': '.csassay.json', 'requirements': 'requirements.json',
                    'testProject': f'tests/{name}.Tests/{name}.Tests.csproj', 'testDll': f'tests/{name}.Tests/bin/Release/net11.0/{name}.Tests.dll', 'workerDll': 'worker/bin/Release/net11.0/Worker.dll', 'contractBaseline': 'contract.json', 'mutations': 'mutations.json', 'minimumTests': 14, 'maximumMutations': 8,
                    'inputs': ['src', 'tests', 'worker', 'App.slnx', '.csassay.json', 'requirements.json', 'contract.json', 'mutations.json', 'assay.app.json', 'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.config', '.editorconfig']}
        write(temporary / 'assay.app.json', manifest)
        (temporary / 'AGENTS.md').write_text('Follow /home/arun/Developer/AGENTS.md when available. This is a pinned net11 functional document-fetch starter. Keep native unions, immutable core data and effect boundaries. Establish a baseline before changing behavior. Keep requirements and contract pins visible; do not suppress rules, lower test minima, exclude survivors, or infer missing evidence. Use the external DAA toolkit for intake, planning and verification; advice does not execute commands or grant Pass.\n')
        (temporary / '.gitignore').write_text('**/bin/\n**/obj/\n.packages/\nevidence/generated/\nTestResults/\n')
        # Commit the complete scaffold atomically, then create package locks in
        # its final location so restore paths do not refer to a temporary root.
        temporary.replace(destination)
    except BaseException:
        shutil.rmtree(temporary, ignore_errors=True)
        raise
    output = destination / 'evidence/generated/bootstrap'
    require(run(['dotnet', 'restore', destination / 'App.slnx', '--force-evaluate'], destination, output / 'restore.log'), 'New application bootstrap restore')
    require(run(['dotnet', 'format', 'whitespace', destination / 'App.slnx', '--no-restore'], destination, output / 'format.log'), 'New application formatting')
    return {'schema': 'assay-new-application/0.1', 'application': str(destination), 'manifest': str(destination / 'assay.app.json'),
            'template': 'Bounded document-fetch starter, not implementation of arbitrary future business requirements.', 'next': 'Run daa verify, then daa plan for the next requirement.'}
