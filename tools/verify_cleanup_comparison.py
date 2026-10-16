#!/usr/bin/env python3
"""Verify real NUnit outcomes and independent evidence for stale data versus TestHarbor quarantine."""
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parent = root / 'examples/TestHarbor.Example/TestResults'
parent.mkdir(parents=True, exist_ok=True)
output = Path(tempfile.mkdtemp(prefix='cleanup-comparison-', dir=parent))
env = dict(os.environ, TESTHARBOR_COMPARISON_OUTPUT=str(output))
result = subprocess.run([env.get('DOTNET', 'dotnet'), 'test', str(root / 'examples/TestHarbor.Example'),
    '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-nodeReuse:false',
    '--filter', 'FullyQualifiedName~CleanupFailureComparison', '--results-directory', str(output),
    '--logger', 'trx;LogFileName=comparison.trx'], env=env, capture_output=True, text=True, timeout=90)
(output / 'runner.log').write_text(result.stdout + result.stderr, encoding='utf-8')
assert result.returncode == 1, result.stdout + result.stderr
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
results = ET.parse(output / 'comparison.trx').findall('.//t:UnitTestResult', ns)
assert len(results) == 6, result.stdout
seen = set()
for item in results:
    log = item.findtext('t:Output/t:StdOut', '', ns)
    match = re.search(r'SCENARIO:(plain|harbor):(\w+)', log)
    assert match, ET.tostring(item)
    mode, method = match.groups()
    assert (mode, method) not in seen
    seen.add((mode, method))
    assert item.attrib['outcome'] == ('Passed' if method == 'Recovery' else 'Failed'), ET.tostring(item)
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    if method == 'NextTestExpectsEmptyTable':
        assert ('quarantin' in message.lower()) if mode == 'harbor' else ('stale test data' in message)
assert seen == {(mode, method) for mode in ('plain', 'harbor')
                for method in ('FirstTestWritesRecord', 'NextTestExpectsEmptyTable', 'Recovery')}
assert (output / 'plain.txt').read_text().splitlines() == [
    'write:1', 'reset-failed', 'read:1', 'reset-ok', 'recover', 'reset-ok', 'recovered-read:0']
assert (output / 'harbor.txt').read_text().splitlines() == [
    'write:1', 'reset-failed', 'recover', 'reset-ok', 'recovered-read:0']
print('PASS: actual NUnit teardown failure permitted stale-data reuse with manual locking; '
      'TestHarbor rejected reuse before the next body and allowed verified recovery.')
print(f'Evidence: {output}')
