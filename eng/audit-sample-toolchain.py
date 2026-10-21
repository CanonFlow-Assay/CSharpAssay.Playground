#!/usr/bin/env python3
"""Evaluate every authored project and exercise strict property rejection."""
import argparse
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {'bin', 'obj', 'TestResults'}


def command(args):
    return subprocess.run([str(x) for x in args], cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'evidence/generated/all-samples/toolchain.json')
    args = parser.parse_args(); args.output.parent.mkdir(parents=True, exist_ok=True)
    report = {'schema': 'assay-all-samples-toolchain/0.1', 'executionStatus': 'running', 'projects': [], 'rejectionChecks': []}
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    try:
        spec = json.loads((ROOT / 'workflows/sample-toolchain.json').read_text())
        pin = json.loads((ROOT / 'global.json').read_text())['sdk']
        sdk = command(['dotnet', '--version'])
        if sdk.returncode or sdk.stdout.strip() != pin['version'] or pin['rollForward'] != 'disable': raise ValueError('Exact SDK pin is not satisfied.')
        report['sdk'] = sdk.stdout.strip()
        projects = sorted(str(p.relative_to(ROOT)) for folder in ('samples', 'src', 'tests') for p in (ROOT / folder).rglob('*.csproj') if not EXCLUDED.intersection(p.relative_to(ROOT).parts))
        declared = sorted(p.attrib['Path'] for p in ET.parse(ROOT / 'AllSamples.slnx').getroot().iter('Project'))
        if projects != declared: raise ValueError('AllSamples.slnx must include every authored project exactly once.')
        exceptions = {x['project']: x['reason'] for x in spec['negativeControls']}
        if not set(exceptions).issubset(projects): raise ValueError('Negative-control ledger contains a missing project.')
        properties = ['TargetFramework', 'TargetFrameworks', 'LangVersion', 'Nullable', 'TreatWarningsAsErrors', 'NETCoreSdkVersion', 'NoWarn']
        for project in projects:
            result = command(['dotnet', 'msbuild', project, '-nologo', '-getProperty:' + ','.join(properties)])
            if result.returncode: raise ValueError('Cannot evaluate ' + project + ': ' + result.stdout[-1000:])
            values = json.loads(result.stdout)['Properties']
            frameworks = (values['TargetFrameworks'] or values['TargetFramework']).split(';')
            if not frameworks or any(t != 'net11.0' and not t.startswith('net11.0-') for t in frameworks) or values['LangVersion'] != 'preview' or values['NETCoreSdkVersion'] != pin['version']:
                raise ValueError('Native toolchain mandate violated by ' + project)
            if project not in exceptions and (values['Nullable'] != 'enable' or values['TreatWarningsAsErrors'].lower() != 'true' or 'CSA' in values['NoWarn'].upper()):
                raise ValueError('Positive-sample compiler mandate violated by ' + project)
            report['projects'].append({'path': project, 'properties': values, 'classification': 'reviewed-negative-control' if project in exceptions else 'strict-positive', 'reason': exceptions.get(project, '')})
        project = ROOT / 'src/Assay.Workflow.Domain/Assay.Workflow.Domain.csproj'
        cases = [('TargetFramework=net10.0', 'CSASSAY-NET11-REQUIRED'), ('TargetFrameworks=net11.0%3Bnet10.0', 'CSASSAY-NET11-REQUIRED'),
                 ('LangVersion=latest', 'CSASSAY-NATIVE-LANGUAGE-REQUIRED'), ('Nullable=disable', 'CSASSAY-NULLABLE-REQUIRED'),
                 ('TreatWarningsAsErrors=false', 'CSASSAY-WARNINGS-AS-ERRORS-REQUIRED'), ('NETCoreSdkVersion=0.0.0', 'CSASSAY-SDK-PIN-REQUIRED')]
        for index, (override, code) in enumerate(cases):
            result = command(['dotnet', 'msbuild', project, '-nologo', '-t:PrepareForBuild', '-p:' + override])
            log = args.output.parent / ('rejection-' + str(index) + '.log'); log.write_text(result.stdout)
            if not result.returncode or code not in result.stdout: raise ValueError('Strict override was not rejected with ' + code + ': ' + result.stdout[-1000:])
            report['rejectionChecks'].append({'override': override, 'expectedCode': code, 'exitCode': result.returncode, 'log': str(log)})
        report.update(executionStatus='completed', sampleProjects=sum(p.startswith('samples/') for p in projects), totalProjects=len(projects), historicalEntries=spec['historicalEntries'])
        args.output.write_text(json.dumps(report, indent=2) + '\n'); print(json.dumps({'executionStatus': 'completed', 'sampleProjects': report['sampleProjects'], 'totalProjects': len(projects), 'strictRejections': len(cases), 'reviewedNegativeControls': len(exceptions)})); return 0
    except BaseException as failure:
        report.update(executionStatus='failed', failure=str(failure)); args.output.write_text(json.dumps(report, indent=2) + '\n'); raise


if __name__ == '__main__': raise SystemExit(main())
