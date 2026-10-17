#!/usr/bin/env python3
"""Verify generic NUnit binding, independent disposal evidence and filtered replay."""
from collections import Counter
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parent = root / 'examples/TestHarbor.Example/TestResults'
parent.mkdir(parents=True, exist_ok=True)
output = Path(tempfile.mkdtemp(prefix='generic-injection-', dir=parent))
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
selection = 'FullyQualifiedName~InjectionGenericMethodTests|FullyQualifiedName~GenericMethodContractTests'


def run(name, filter_text, count, failed=False, discovery=False):
    """Require real adapter results and retain independently recorded service disposal evidence."""
    folder = output / name
    folder.mkdir()
    evidence = folder / 'disposal'
    evidence.mkdir()
    command = [os.environ.get('DOTNET', 'dotnet'), 'test', str(root / 'examples/TestHarbor.Example'),
               '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-nodeReuse:false',
               '--filter', filter_text, '--results-directory', str(folder), '--logger', 'trx;LogFileName=results.trx']
    if discovery:
        command.append('--list-tests')
    result = subprocess.run(command, env=dict(os.environ, TESTHARBOR_GENERIC_OUTPUT=str(evidence)),
                            capture_output=True, text=True, timeout=120)
    (folder / 'runner.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    assert result.returncode == int(failed), result.stdout + result.stderr
    records = [json.loads(path.read_text()) for path in evidence.glob('*.json')]
    if discovery:
        assert not records, 'Generic discovery constructed or disposed services'
        assert 'Inferred<Int32>(7)' in result.stdout and 'NamedGenericString' in result.stdout, result.stdout
        return [], records
    results = ET.parse(folder / 'results.trx').findall('.//t:UnitTestResult', ns)
    assert len(results) == count, [x.attrib for x in results]
    assert all(x.attrib['outcome'] == ('Failed' if failed else 'Passed') for x in results), [x.attrib for x in results]
    return results, records


run('discovery', selection, 0, discovery=True)
results, records = run('normal', selection, 32)
assert all('GenericRepository' not in x.attrib['testName'] and 'KeyedEndpoint' not in x.attrib['testName'] for x in results), 'Services leaked into case identity'
expected_disposals = {'ClosedService': 2, 'KeyedService': 4, 'Collection': 2, 'TwoTypes': 4,
                      'Constrained': 1, 'ServiceOnly': 2, 'TaskResult': 2, 'ValueTaskResult': 2,
                      'TypedSource': 1, 'InheritedGeneric': 4, 'Retried': 4, 'Repeated': 6, 'NamedGeneric': 2}
assert Counter(x['method'] for x in records) == expected_disposals, records
assert len({x['instance'] for x in records}) == 36, records
for method, attempts in [('Retried', 2), ('Repeated', 3)]:
    rows = [x for x in records if x['method'] == method]
    assert {x['type'] for x in rows} == {'System.Int32', 'System.String'}, rows
    for value_type in ('System.Int32', 'System.String'):
        typed = [x for x in rows if x['type'] == value_type]
        assert len(typed) == attempts and {x['attempt'] for x in typed} == set(range(attempts)), typed
        assert all(x['count'] == 1 for x in typed), typed
replay, replay_records = run('named-replay', 'TestCategory=GenericReplay&Name=NamedGenericInt', 1)
assert replay[0].attrib['testId'] in {x.attrib['testId'] for x in results}, 'Named generic case identity changed'
assert len(replay_records) == 1 and replay_records[0]['type'] == 'System.Int32', replay_records
invalid, invalid_records = run('invalid', 'FullyQualifiedName~GenericMethodInvalid', 2, failed=True)
assert not invalid_records, invalid_records
for item in invalid:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert 'unreachable' not in message and 'ignored-profile-activated' not in message, message
    expected = 'violates the constraint' if item.attrib['testName'] == 'Constraint' else 'Unable to determine type arguments'
    assert expected in message, message
for method, count, message_part, concrete_type in [
        ('Missing', 2, 'expected exactly one registration; found 0', 'System.Int32'),
        ('Ambiguous', 5, 'expected exactly one registration; found 3', 'System.Int32'),
        ('NullFactory', 2, 'registered factory returned null', 'System.Int64')]:
    failures, disposed = run('failure-' + method, 'FullyQualifiedName~GenericMethodFailureProbes.' + method, 1, failed=True)
    message = failures[0].findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert message_part in message and concrete_type in message and method in message, message
    assert 'unreachable' not in message and 'ignored-profile-activated' not in message, message
    assert len(disposed) == count and all(x['count'] == 0 for x in disposed), disposed
    assert Counter(x['type'] for x in disposed) == ({'System.String': 1, 'System.Double': 1, 'System.Int32': 3}
           if method == 'Ambiguous' else {'System.String': 1, 'System.Double': 1}), disposed
print('PASS: 32 generic cases, 36 independently recorded disposals, retry/repeat isolation, stable named replay, two discovery errors and three partial-resolution cleanup probes.')
