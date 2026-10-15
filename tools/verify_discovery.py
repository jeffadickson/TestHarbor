#!/usr/bin/env python3
"""Verify stable discovery-time cases, filtered replay and the runtime-node boundary through the adapter."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
command = [os.environ.get('DOTNET', 'dotnet'), 'test', str(root / 'tests/TestHarbor.Tests'), '-c', 'Release',
           '--no-build', '--no-restore', '-m:1', '-nodeReuse:false']
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
with tempfile.TemporaryDirectory(prefix='testharbor-discovery-') as directory:
    results = []
    for index, selection in enumerate(('FullyQualifiedName~DiscoveryBoundaryTests', 'FullyQualifiedName~DiscoveryBoundaryTests.Discovered')):
        report = f'discovery{index}.trx'
        run = subprocess.run(command + ['--filter', selection, '--results-directory', directory, '--logger', 'trx;LogFileName=' + report],
                             capture_output=True, text=True, timeout=90)
        assert run.returncode == 0, run.stdout + run.stderr
        cases = ET.parse(Path(directory) / report).findall('.//t:UnitTestResult', ns)
        assert all(c.attrib['outcome'] == 'Passed' for c in cases)
        results.append(sorted(c.attrib['testName'] for c in cases))
    assert len(results[0]) == 3 and len(results[1]) == 2, results
    assert results[1] == [name for name in results[0] if name.startswith('Discovered[')], results
    assert 'RuntimeItems' in results[0]
    print('PASS: two stable discovery cases replay under filtering; runtime work remains one NUnit result.')
    fixture_run = subprocess.run([os.environ.get('DOTNET', 'dotnet'), 'test', str(root / 'examples/TestHarbor.Example'),
                                 '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-nodeReuse:false',
                                 '--filter', 'FullyQualifiedName~SharedScopeTests', '--results-directory', directory,
                                 '--logger', 'trx;LogFileName=shared.trx'], capture_output=True, text=True, timeout=90)
    assert fixture_run.returncode == 0, fixture_run.stdout + fixture_run.stderr
    cases = ET.parse(Path(directory) / 'shared.trx').findall('.//t:UnitTestResult', ns)
    assert len(cases) == 2 and all(c.attrib['outcome'] == 'Passed' for c in cases)
    print('PASS: filtered fixture-owner initialization, parallel child scopes and final owner cleanup.')
