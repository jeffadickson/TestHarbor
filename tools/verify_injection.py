#!/usr/bin/env python3
"""Verify runtime injection through real NUnit discovery, filtered replay and deliberate failures."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
parent = root / 'examples/TestHarbor.Example/TestResults'
parent.mkdir(parents=True, exist_ok=True)
output = Path(tempfile.mkdtemp(prefix='injection-', dir=parent))
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
normal_filter = 'FullyQualifiedName~InjectedOrderTests|FullyQualifiedName~InjectionContractTests'


def run(name, selection, expected_exit=0, discovery=False):
    """Run one adapter invocation and retain logs, TRX and independent lifecycle evidence."""
    folder = output / name
    folder.mkdir()
    command = [os.environ.get('DOTNET', 'dotnet'), 'test', str(root / 'examples/TestHarbor.Example'),
               '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-nodeReuse:false',
               '--filter', selection, '--results-directory', str(folder), '--logger', 'trx;LogFileName=results.trx']
    if discovery:
        command.append('--list-tests')
    result = subprocess.run(command, env=dict(os.environ, TESTHARBOR_INJECTION_OUTPUT=str(folder)),
                            capture_output=True, text=True, timeout=120)
    (folder / 'runner.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    assert result.returncode == expected_exit, result.stdout + result.stderr
    if discovery:
        assert not list(folder.glob('*.json*')), 'Discovery activated services or constructors'
        assert 'SavesOrder' in result.stdout and 'NullProduct' in result.stdout, result.stdout
        return folder, []
    results = ET.parse(folder / 'results.trx').findall('.//t:UnitTestResult', ns)
    return folder, results


def evidence(folder):
    """Load per-service lifecycle streams and post-release coordinator snapshots."""
    streams = [[json.loads(line) for line in path.read_text().splitlines()] for path in folder.glob('*.jsonl')]
    finished = [json.loads(path.read_text()) for path in folder.glob('finished-*.json')]
    assert all(not x['held'] and x['waiting'] == 0 for x in finished), finished
    return streams, finished


run('discovery', 'FullyQualifiedName~Injection', discovery=True)
folder, results = run('normal', normal_filter)
assert len(results) == 8 and all(x.attrib['outcome'] == 'Passed' for x in results), [x.attrib for x in results]
streams, finished = evidence(folder)
assert len(streams) == len(finished) == 10, (len(streams), len(finished))
assert all(x['quarantine'] is None for x in finished), finished
for stream in streams:
    phases = [x['phase'] for x in stream]
    if stream[0]['method'] == 'Inherited':
        assert phases == ['start', 'inherited-body', 'stop', 'service-dispose'], phases
    else:
        assert phases == ['start', 'constructor', 'setup', 'body', 'teardown', 'fixture-dispose', 'stop', 'service-dispose'], phases
for method in ('Repeated', 'Retried'):
    attempts = [s[0]['attempt'] for s in streams if s[0]['method'] == method]
    assert sorted(attempts) == [0, 1], (method, attempts)

folder, replay = run('filtered', 'FullyQualifiedName~InjectedOrderTests.SavesOrder&Name~book')
assert len(replay) == 1 and replay[0].attrib['outcome'] == 'Passed', [x.attrib for x in replay]
assert replay[0].attrib['testId'] in {x.attrib['testId'] for x in results}, 'Filtered case identity changed'
assert len(evidence(folder)[0]) == 1, 'Filtered run initialized unselected fixtures'

_, checkout_results = run('checkout-comparison', 'FullyQualifiedName~CheckoutTests')
assert len(checkout_results) == 8 and all(x.attrib['outcome'] == 'Passed' for x in checkout_results), [x.attrib for x in checkout_results]
_, checkout_replay = run('checkout-filtered', 'FullyQualifiedName~InjectedCheckoutTests&Name~2700')
assert len(checkout_replay) == 1 and checkout_replay[0].attrib['outcome'] == 'Passed', [x.attrib for x in checkout_replay]
assert checkout_replay[0].attrib['testId'] in {x.attrib['testId'] for x in checkout_results}, 'Checkout case identity changed'
print('PASS: eight equivalent checkout cases; injected discount case replays with stable identity.')

folder, failures = run('failures', 'FullyQualifiedName~InjectionFailureProbes', expected_exit=1)
assert len(failures) == 17, [x.attrib for x in failures]
markers = {'ConstructorFails': 'constructor-marker', 'StartupFails': 'startup-marker', 'SetupFails': 'setup-marker',
           'BodyFails': 'body-marker', 'TeardownFails': 'teardown-marker', 'FixtureDisposeFails': 'fixture-dispose-marker',
           'CleanupFails': 'cleanup-marker', 'BodyAndCleanupFail': 'primary-marker',
           'ConstructorAndCleanupFail': 'constructor-marker', 'ServiceDisposeFails': 'service-dispose-marker',
           'MissingService': "parameter 'missing'", 'AmbiguousService': "parameter 'duplicate'"}
for item in failures:
    name = item.attrib['testName']
    expected = 'NotExecuted' if name in ('Skipped', 'Inconclusive', 'ConditionSkips') else 'Failed'
    assert item.attrib['outcome'] == expected, item.attrib
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert 'unreachable-body' not in message, message
    if name in markers:
        assert markers[name] in message, (name, message)
    if name in ('BodyAndCleanupFail', 'ConstructorAndCleanupFail'):
        assert 'cleanup-marker' in message, message
streams, finished = evidence(folder)
assert len(streams) == len(finished) == 16, (len(streams), len(finished))
quarantined = {'TeardownFails', 'FixtureDisposeFails', 'CleanupFails', 'BodyAndCleanupFail', 'ConstructorAndCleanupFail', 'ServiceDisposeFails'}
assert {x['method'] for x in finished if x['quarantine'] is not None} == quarantined, finished
for stream in streams:
    method = stream[0]['method']
    phases = [x['phase'] for x in stream]
    expected = ['start']
    if method not in ('StartupFails', 'StartupCancelled'):
        expected += ['constructor']
        if method not in ('ConstructorFails', 'ConstructorAndCleanupFail'):
            expected += ['setup']
            if method != 'SetupFails':
                if method not in ('MissingService', 'AmbiguousService'):
                    expected += ['body']
            expected += ['teardown']
            expected += ['fixture-dispose']
    expected += ['stop', 'service-dispose']
    assert phases == expected, (method, phases, expected)

folder, invalid = run('invalid', 'FullyQualifiedName~InjectionInvalid', expected_exit=1)
assert len(invalid) == 3 and all(x.attrib['outcome'] == 'Failed' for x in invalid), [x.attrib for x in invalid]
assert not list(folder.glob('*.json*')), 'Invalid declarations activated runtime services'
for item in invalid:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert 'unreachable-body' not in message and any(x in message for x in ('ref/out', 'SingleInstance', 'exactly one public constructor')), message
print('PASS: injection discovery without activation; 8 cases/10 fresh attempts; filtered stable identity; '
      '17 failure/skip/cancellation probes; 3 invalid declarations; cleanup order and quarantine verified.')
print(f'Evidence: {output}')
