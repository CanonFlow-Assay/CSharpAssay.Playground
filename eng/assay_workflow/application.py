"""Reusable application assessment: independent engine authority and preview modules."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from .runner import *


def artifact(path, output):
    return {'path': str(Path(path).relative_to(output)), 'sha256': sha(path)}


def make_assessment(snapshot_id, requirements_id, support, baseline, requirements, modules):
    return {'schema': 'assay-application-assessment/0.1', 'snapshotSha256': snapshot_id, 'requirementsSha256': requirements_id,
            'support': support, 'baseline': baseline, 'requirements': requirements['requirements'], 'modules': modules}


def decision(assessment, output, proposed='', proposal_snapshot='', budget=3):
    registry = [{'id': probe, 'requirementId': r['id'], 'module': {'behavior': 'Behavior', 'contract': 'ContractAssay', 'resilience': 'ResilienceAssay'}[probe]}
                for r in assessment['requirements'] for probe in r['probeIds'] if probe in ('behavior', 'contract', 'resilience')]
    # The same execution may investigate two requirements; choose one registered
    # requirement binding per action rather than ambiguous duplicate action IDs.
    registry = list({item['id']: item for item in registry}.values())
    write(output / 'decision-input.json', {'assessment': assessment, 'registry': registry, 'remainingProbes': budget, 'proposedProbe': proposed, 'proposalSnapshot': proposal_snapshot})
    execution = run(['dotnet', DECISION, 'decide', output / 'decision-input.json'], TOOLKIT, output / 'decision.log', 30)
    require(execution, 'Typed Decision Assist')
    value = json.loads(execution.output)
    if value['schema'] != 'assay-application-decision/0.1' or value['snapshotSha256'] != assessment['snapshotSha256']:
        raise ValueError('Decision identity/schema mismatch.')
    write(output / 'decision.json', value)
    return value


def coverage_observations(root, files, coverage_path, mutations, previous):
    hits = {}
    if coverage_path.is_file():
        tree = ET.parse(coverage_path)
        for cls in tree.findall('.//class'):
            filename = cls.get('filename', '').replace('\\', '/')
            relative = filename.removeprefix('/_/')
            if Path(filename).is_absolute() and Path(filename).is_relative_to(root):
                relative = str(Path(filename).relative_to(root))
            for line in cls.findall('./lines/line'):
                key = (relative, line.get('number'))
                hits[key] = max(hits.get(key, 0), int(line.get('hits', '0')))
    observations = []
    for name in sorted(files):
        if not name.endswith('.cs') or 'tests/' in name.lower(): continue
        rows = [v for (path, _), v in hits.items() if path == name]
        lexical = len(re.findall(r'\b(if|case|catch|while|for|foreach)\b', (root / name).read_text()))
        observations.append({'id': name, 'changed': bool(previous) and previous.get(name) != files[name], 'coverageKnown': bool(rows),
                             'lineCoverage': round(100 * sum(v > 0 for v in rows) / len(rows), 2) if rows else 0,
                             'complexity': lexical, 'survivingMutants': sum(m['path'] == name and m['state'] == 'Survived' for m in mutations)})
    return observations


def verify(manifest_path, output, baseline_path=None, mutations=True):
    output = Path(output).resolve(); output.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'assay-application-run/0.1', 'executionStatus': 'running', 'providerRequests': 0,
              'authority': 'Application coordination and bounded preview checks. Underlying CSharpAssay receipt retains its own authority; ReadyForReview is not release approval.'}
    write(output / 'run.json', report)
    try:
        root, manifest, requirements = load_manifest(manifest_path)
        previous_files = {}; previous_tests = None; previous_contract = None
        if baseline_path:
            old_path = Path(baseline_path).resolve(); old = read(old_path)
            if old.get('schema') != 'assay-application-run/0.1' or old.get('executionStatus') != 'completed' or old.get('workflowStatus') != 'ReadyForReview' or old.get('projectId') != manifest['id']:
                raise ValueError('A complete ready baseline from this application is required.')
            previous_files = old['inputSha256']
            for name in ('behavior.json', 'pinned-contract.json'):
                if sha(old_path.parent / name) != old['evidenceSha256'][name]: raise ValueError('Prior baseline artifact changed: ' + name)
            previous_tests = read(old_path.parent / 'behavior.json'); previous_contract = read(old_path.parent / 'pinned-contract.json')
            if manifest['minimumTests'] < previous_tests['total']: raise ValueError('Candidate lowers the recorded baseline test minimum.')
        files, source_id = snapshot(root, manifest); requirement_id = sha(inside(root, manifest['requirements']))
        tools = toolkit_identity(); modules = []
        observed = intake(root, manifest, output / 'intake'); write(output / 'intake.json', observed)
        report.update(projectId=manifest['id'], projectRoot=str(root), manifestPath=str(Path(manifest_path).resolve()), inputSha256=files, snapshotSha256=source_id, requirementsSha256=requirement_id, toolkitSha256=tools)
        def add(module, state, required, authoritative, paths, notes):
            modules.append({'module': module, 'state': state, 'required': required, 'authoritative': authoritative,
                            'snapshotSha256': source_id, 'requirementsSha256': requirement_id, 'artifacts': [artifact(p, output) for p in paths], 'observations': notes})
        baseline_state = 'NotRun'
        if observed['support'] == 'Native':
            require(run(['dotnet', 'restore', inside(root, manifest['solution']), '--locked-mode'], root, output / 'restore.log'), 'Locked application restore')
            require(run(['dotnet', 'build', inside(root, manifest['solution']), '--no-restore', '-c', 'Release'], root, output / 'build.log'), 'Application build')
            baseline = tests(root, manifest, output / 'behavior', coverage=True); baseline_state = baseline['state']; write(output / 'behavior.json', baseline)
            if previous_tests and not set(previous_tests['testNames']).issubset(baseline.get('testNames', [])): raise ValueError('Previously recorded tests disappeared from the candidate selection.')
            add('Behavior', baseline_state, True, False, [output / 'behavior.json', output / 'behavior/execution.log'], ['Declared xUnit v3 selection; no skipped tests or execution gaps allowed.'])
            if baseline_state == 'Passed':
                engine_path = output / 'engine-assessment.json'
                execution = run(['dotnet', ENGINE, 'assess', inside(root, manifest['solution']), '--policy', inside(root, manifest['policy']), '--profile', 'native', '--json', engine_path], root, output / 'engine.log', 300)
                if engine_path.is_file():
                    envelope = read(engine_path); receipt = envelope['receipt']; value = receipt['evidence']
                    authoritative = envelope['assessment']['authoritative'] and value['authoritative']
                    if envelope['assessment']['schema'] != 'csassay-assessment/0.1' or envelope['assessment']['verdict'].lower() != receipt['verdict'].lower():
                        raise ValueError('Engine assessment projection contradicts its receipt.')
                    engine_state = {'pass': 'Passed', 'fail': 'Failed', 'inconclusive': 'NotRun', 'toolFailure': 'ToolFailure'}.get(receipt['verdict'], 'ToolFailure')
                    if execution.state in ('TimedOut', 'Cancelled'): engine_state = execution.state
                    if engine_state == 'Passed' and (execution.exit_code != 0 or not authoritative or value['missingEvidence'] or value['failures']): raise ValueError('Engine receipt lacks complete passing authority.')
                    add('CSharpAssay', engine_state, True, authoritative, [engine_path], [str(len(value['findings'])) + ' retained findings; underlying receipt supplies rule authority.'])
                else:
                    add('CSharpAssay', execution.state if execution.state in ('TimedOut', 'Cancelled') else 'ToolFailure', True, False, [output / 'engine.log'], ['No engine assessment artifact.'])
                require(execution, 'Authoritative engine assessment')
                execution = run(['dotnet', inside(root, manifest['workerDll'])], root, output / 'worker.log', 60)
                require(execution, 'Controlled consumer execution')
                worker = json.loads(execution.output)
                if worker.get('executionStatus') != 'completed' or worker.get('schema') != 'polly-consumer-execution/0.1' or len(worker['scenarios']) != 16 or len(worker['infrastructureChecks']) != 6:
                    raise ValueError('Incomplete controlled worker execution.')
                write(output / 'consumer.json', worker)
                pinned = read(inside(root, manifest['contractBaseline']))
                if previous_contract is not None: pinned = previous_contract
                write(output / 'pinned-contract.json', pinned); report['pinnedContractSha256'] = sha(output / 'pinned-contract.json')
                mutation_report = preview_mutations(root, manifest, output / 'mutation', baseline['total']) if mutations else {'executionStatus': 'not-requested', 'results': [], 'unexpectedOutcomes': []}
                write(output / 'mutation.json', mutation_report)
                rows = mutation_report['results']
                state = 'NotRun' if not mutations else ('Failed' if mutation_report['unexpectedOutcomes'] else ('Survived' if any(r['state'] == 'Survived' for r in rows) else 'Passed'))
                if any(r['state'] in ('Invalid', 'TimedOut', 'Cancelled', 'ToolFailure') for r in rows): state = next(r['state'] for r in rows if r['state'] in ('Invalid', 'TimedOut', 'Cancelled', 'ToolFailure'))
                add('MutationAssay', state, False, False, [output / 'mutation.json'], ['All survivors retained; preview is advisory.'])
                traces = [t for scenario in worker['scenarios'] if scenario['pipeline'] == 'functional' for t in scenario['trace']]
                module_input = {'baselineContract': pinned, 'candidateContract': worker['observedContract'],
                                'risk': coverage_observations(root, files, output / 'behavior/coverage.cobertura.xml', rows, previous_files),
                                'trace': traces, 'traceContract': {'maximumAttempts': 3, 'deadlineTicks': 20_000_000, 'allowsFallback': False, 'atMostOneSideEffect': True}}
                write(output / 'module-input.json', module_input)
                computed = run(['dotnet', DECISION, 'modules', output / 'module-input.json'], TOOLKIT, output / 'modules.log', 30)
                require(computed, 'Preview evidence modules')
                values = json.loads(computed.output); write(output / 'modules.json', values)
                add('ContractAssay', 'Failed' if values['contractDifferences'] else 'Passed', True, False, [output / 'modules.json', output / 'pinned-contract.json'], ['Limited public response members and projected outcome comparison; not universal API/serialization compatibility.'])
                add('ResilienceAssay', 'Failed' if values['resilienceIssues'] else 'Passed', True, False, [output / 'consumer.json', output / 'modules.json'], ['16 controlled consumer executions and six infrastructure checks; not all Polly internals.'])
                add('CodeRiskAssay', 'Passed', False, False, [output / 'modules.json'], ['Declared ordinal heuristic; complexity is a lexical branch count, not semantic cyclomatic complexity. Unknown coverage remains unknown.'])
        assessment = make_assessment(source_id, requirement_id, observed['support'], baseline_state, requirements, modules)
        advice = decision(assessment, output)
        if snapshot(root, manifest) != (files, source_id) or toolkit_identity() != tools: raise ValueError('Inputs/toolkit changed while executing; completion is invalid.')
        report.update(executionStatus='completed', assessment=assessment, workflowStatus=advice['workflowStatus'], decision=advice['decision'],
                      evidenceSha256={str(p.relative_to(output)): sha(p) for p in output.rglob('*') if p.is_file() and p.name != 'run.json' and not p.name.endswith('.partial')},
                      reviewStatus='Agent-reviewed requirements and preview qualification; independent human review pending.')
        write(output / 'run.json', report)
        write(output / 'probe-budget.json', {'snapshotSha256': source_id, 'remaining': 3})
        return report
    except BaseException as failure:
        report.update(executionStatus='cancelled' if isinstance(failure, KeyboardInterrupt) else 'failed', failure=str(failure))
        write(output / 'run.json', report)
        raise


def plan(manifest_path, requirement_path, output):
    root, manifest, requirements = load_manifest(manifest_path)
    files, source_id = snapshot(root, manifest); request = read(requirement_path)
    if request.get('schema') != 'assay-change-request/0.1' or not request.get('description', '').strip() or not request.get('acceptanceCriteria'):
        raise ValueError('A change request needs a description and explicit acceptance criteria.')
    value = {'schema': 'assay-coding-work-item/0.1', 'snapshotSha256': source_id, 'inputSha256': files,
             'requirementsSha256': sha(inside(root, manifest['requirements'])), 'requestSha256': sha(requirement_path),
             'request': request, 'status': 'implementation-required', 'allowedInvestigationIds': sorted({p for r in requirements['requirements'] for p in r['probeIds']}),
             'obligations': ['Establish passing baseline', 'Preserve pinned public contract unless explicitly reviewed', 'Implement explicit native outcomes and effect boundaries', 'Add tests against independently stated criteria', 'Run daa verify with the previous baseline receipt', 'Retain all failures, advisories and survivors'],
             'authority': 'Coding-agent handoff only; this work item does not claim the requested behavior is implemented or verified.'}
    write(output, value); return value
