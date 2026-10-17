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

folder, diagnostics = run('diagnostics', 'FullyQualifiedName~InjectionDiagnosticProbes|FullyQualifiedName~InjectionConstructorDiagnostic', expected_exit=1)
assert len(diagnostics) == 8, [x.attrib for x in diagnostics]
for item in diagnostics:
    name = item.attrib['testName']
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert 'unreachable-body' not in message and 'unreachable-constructor' not in message, message
    if name == 'FactorySkips':
        assert item.attrib['outcome'] == 'NotExecuted', item.attrib
        assert 'factory-skip-marker' in message and 'InjectionResolutionException' not in message, message
        continue
    assert item.attrib['outcome'] == 'Failed', item.attrib
    assert 'InjectionResolutionException' in message and "parameter 'service'" in message, message
    member = '.ctor' if name == 'ConstructorGraphFails' else name
    assert member in message, message
    expected = {'NestedMissing': 'MissingDependency', 'ConstructorGraphFails': 'MissingDependency',
                'Circular': 'circular dependency', 'FactoryFails': 'factory-error-marker',
                'FactoryNull': 'factory returned null', 'CollectionFails': 'collection-error-marker',
                'CollectionNull': 'null element'}[name]
    assert expected in message, message
streams, finished = evidence(folder)
assert len(streams) == len(finished) == 8, (len(streams), len(finished))
assert all(x['quarantine'] is None for x in finished), finished
for stream in streams:
    expected = ['start', 'stop']
    if stream[0]['method'] in ('CollectionFails', 'CollectionNull'):
        expected.append('collection-dispose')
    expected.append('service-dispose')
    assert [x['phase'] for x in stream] == expected, stream
print('PASS: eight injection graph diagnostics retain parameter context, original cause, skip semantics and completed cleanup.')

_, collection_results = run('collections', 'FullyQualifiedName~InjectionCollectionTests')
assert len(collection_results) == 5 and all(x.attrib['outcome'] == 'Passed' for x in collection_results), [x.attrib for x in collection_results]
print('PASS: collection injection preserves order, scoped identity, transient semantics, empty collections and parallel case isolation.')

_, keyed_results = run('keyed', 'FullyQualifiedName~InjectionKeyed')
assert len(keyed_results) == 8 and all(x.attrib['outcome'] == 'Passed' for x in keyed_results), [x.attrib for x in keyed_results]
_, keyed_replay = run('keyed-filtered', 'FullyQualifiedName~InjectionKeyedMethodTests.RoutesData&Name~read')
assert len(keyed_replay) == 1 and keyed_replay[0].attrib['outcome'] == 'Passed', [x.attrib for x in keyed_replay]
assert keyed_replay[0].attrib['testId'] in {x.attrib['testId'] for x in keyed_results}, 'Keyed case identity changed'
folder, keyed_failures = run('keyed-failures', 'FullyQualifiedName~KeyedFailureProbes', expected_exit=1)
assert len(keyed_failures) == 5, [x.attrib for x in keyed_failures]
for item in keyed_failures:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert item.attrib['outcome'] == 'Failed' and 'InjectionResolutionException' in message, message
    assert "parameter 'service'" in message and 'unreachable-body' not in message, message
    expected = {'MissingKey': 'found 0', 'DuplicateKey': 'found 2', 'NullKeyedService': 'factory returned null',
                'KeyedCollectionFails': 'keyed-collection-marker', 'KeyedCollectionNull': 'null element'}[item.attrib['testName']]
    assert expected in message, message
streams, finished = evidence(folder)
assert len(streams) == len(finished) == 5 and all(x['quarantine'] is None for x in finished), finished
for stream in streams:
    expected = ['start', 'stop']
    if stream[0]['method'].startswith('KeyedCollection'):
        expected.append('collection-dispose')
    expected.append('service-dispose')
    assert [x['phase'] for x in stream] == expected, stream
folder, keyed_invalid = run('keyed-invalid', 'FullyQualifiedName~KeyedInvalid', expected_exit=1)
assert len(keyed_invalid) == 3 and all(x.attrib['outcome'] == 'Failed' for x in keyed_invalid), [x.attrib for x in keyed_invalid]
for item in keyed_invalid:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert any(marker in message for marker in ('not both', 'explicit key', 'ServiceKey is unsupported')), message
    assert 'unreachable' not in message, message
print('PASS: eight keyed cases, stable filtered replay, five cleanup/failure probes and three invalid declarations.')

_, data_results = run('data-compatibility', 'FullyQualifiedName~InjectionDataCompatibilityTests|FullyQualifiedName~InjectionValueTaskTests|FullyQualifiedName~InjectionGenericContractTests|FullyQualifiedName~PlainDataConversionTests')
assert len(data_results) == 38 and all(x.attrib['outcome'] == 'Passed' for x in data_results), [x.attrib for x in data_results]
assert all('KeyedEndpoint' not in x.attrib['testName'] for x in data_results), 'Service leaked into case identity'


def observed_rows(results, prefix):
    """Read each case's observations once, excluding duplicated run-level output."""
    rows = []
    for result in results:
        for line in result.findtext('t:Output/t:StdOut', '', ns).splitlines():
            if line.startswith(prefix):
                rows.append(json.loads(line[len(prefix):]))
    return rows


import itertools
pairwise = observed_rows(data_results, 'PAIRWISE:')
assert len(pairwise) == 4, pairwise
axes = [['east', 'west'], [1, 2], [False, True]]
for a, b in itertools.combinations(range(3), 2):
    assert {(row[a], row[b]) for row in pairwise} == set(itertools.product(axes[a], axes[b])), pairwise
automatic = observed_rows(data_results, 'AUTOMATIC:')
assert len(automatic) == 6 and {tuple(row) for row in automatic} == set(itertools.product([0, 1], [None, False, True])), automatic
_, source_replay = run('async-source-filtered', 'FullyQualifiedName~InjectionDataCompatibilityTests.TaskSource&Name~TaskSourceTwo')
assert len(source_replay) == 1 and source_replay[0].attrib['outcome'] == 'Passed', [x.attrib for x in source_replay]
assert source_replay[0].attrib['testId'] in {x.attrib['testId'] for x in data_results}, 'Async source case identity changed'
_, invalid_data = run('invalid-data-services', 'FullyQualifiedName~DataBindingInvalid', expected_exit=1)
assert len(invalid_data) == 3, [x.attrib for x in invalid_data]
for item in invalid_data:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert item.attrib['outcome'] == 'Failed' and 'service parameters cannot also declare' in message, message
    assert 'unreachable' not in message, message
print('PASS: 38 data/async/inheritance cases, exhaustive pair coverage, nullable enum expansion, stable async-source replay and three invalid service/data declarations.')

_, argument_results = run('arguments', 'FullyQualifiedName~InjectionArgumentTests')
assert len(argument_results) == 20 and all(x.attrib['outcome'] == 'Passed' for x in argument_results), [x.attrib for x in argument_results]
assert all('KeyedEndpoint' not in x.attrib['testName'] and 'IServiceProvider' not in x.attrib['testName'] for x in argument_results), 'Service leaked into argument case names'
_, category_replay = run('argument-category', 'TestCategory=InjectionMetadata')
assert len(category_replay) == 1 and category_replay[0].attrib['outcome'] == 'Passed', [x.attrib for x in category_replay]
assert category_replay[0].attrib['testName'] == 'NamedCategory', category_replay[0].attrib
assert category_replay[0].attrib['testId'] in {x.attrib['testId'] for x in argument_results}, 'Category replay changed case identity'
_, ignored = run('ignored-activation', 'FullyQualifiedName~InjectionIgnoredTests')
assert len(ignored) == 2 and all(x.attrib['outcome'] == 'NotExecuted' for x in ignored), [x.attrib for x in ignored]
_, invalid_arguments = run('invalid-arguments', 'FullyQualifiedName~InjectionArgumentInvalid', expected_exit=1)
assert len(invalid_arguments) == 4, [x.attrib for x in invalid_arguments]
for item in invalid_arguments:
    message = item.findtext('t:Output/t:ErrorInfo/t:Message', '', ns)
    assert item.attrib['outcome'] == 'Failed' and 'unreachable-body' not in message, message
    if item.attrib['testName'] == 'KeyedProvider':
        assert 'expected exactly one registration; found 0' in message, message
    else:
        assert 'without ref/out parameters or optional/params services' in message, message
print('PASS: 20 argument/provider cases, category replay identity, two ignored activation guards and four unsupported declaration/key probes.')
