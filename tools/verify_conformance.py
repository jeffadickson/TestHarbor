#!/usr/bin/env python3
"""Verify real-runner extension events and final outcomes, including skip and assumption handling."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='testharbor-conformance-') as directory:
    env = dict(os.environ, TESTHARBOR_PROBE_OUTPUT=directory)
    result = subprocess.run([env.get('DOTNET', 'dotnet'), 'test', str(root / 'tests/TestHarbor.Tests'),
                             '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-nodeReuse:false',
                             '--filter', 'FullyQualifiedName~ConformanceProbes', '--results-directory', directory,
                             '--logger', 'trx;LogFileName=conformance.trx'], env=env, capture_output=True, text=True, timeout=90)
    report = Path(directory) / 'conformance.trx'
    if result.returncode != 1 or not report.exists():
        raise SystemExit(result.stdout + result.stderr)
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    outcomes = ET.parse(report).findall('.//t:UnitTestResult', ns)
    assert len(outcomes) == 10, result.stdout
    for mode in ('pass', 'skip', 'assume', 'start', 'stop', 'cancel', 'deadline'):
        outcome = next(x for x in outcomes if f'("{mode}")' in x.attrib['testName'])
        expected = 'Passed' if mode == 'pass' else 'NotExecuted' if mode in ('skip', 'assume') else 'Failed'
        assert outcome.attrib['outcome'] == expected, ET.tostring(outcome)
        phases = ['start', 'stop', 'dispose'] if mode in ('start', 'cancel') else ['start', 'setup', 'body', 'teardown', 'stop', 'dispose']
        assert (Path(directory) / (mode + '.txt')).read_text().splitlines() == phases, mode
    for mode in ('conditionSkip', 'conditionAssume', 'conditionFail'):
        outcome = next(x for x in outcomes if f'("{mode}")' in x.attrib['testName'])
        assert outcome.attrib['outcome'] == ('Failed' if mode == 'conditionFail' else 'NotExecuted')
        assert not (Path(directory) / (mode + '.txt')).exists(), 'Condition acquired hardware or initialized services'
    print('PASS: ten conformance scenarios; exact event ordering, retained leases and final runner outcomes verified.')
