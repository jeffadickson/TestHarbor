#!/usr/bin/env python3
"""Run the same complete Release validation locally and in every CI environment."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def run(label, command, environment):
    """Run one gate from the repository root and stop immediately on failure."""
    print(f'\n== {label} ==', flush=True)
    # Argument lists preserve paths with spaces without invoking a shell.
    result = subprocess.run(command, cwd=ROOT, env=environment)
    if result.returncode:
        raise SystemExit(f'FAIL: {label} (exit {result.returncode}). See output above.')


def doctor():
    """Resolve the SDK override and verify global.json selects a .NET 10 SDK."""
    requested = os.environ.get('DOTNET', 'dotnet')
    executable = shutil.which(requested)
    if executable is None:
        raise SystemExit('Install the .NET 10 SDK or set DOTNET to its executable path.')
    executable = str(Path(executable).resolve())
    result = subprocess.run([executable, '--version'], cwd=ROOT, capture_output=True, text=True)
    if result.returncode or not result.stdout.strip().startswith('10.'):
        raise SystemExit('The repository requires a .NET 10 SDK compatible with global.json.\n'
                         + result.stdout + result.stderr)
    print(f'Python: {sys.version.split()[0]} ({sys.executable})', flush=True)
    print(f'.NET SDK: {result.stdout.strip()} ({executable})', flush=True)
    return executable


def main():
    """Check prerequisites, then execute all required gates unless diagnosis alone is requested."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--doctor', action='store_true', help='Check prerequisites without building.')
    options = parser.parse_args()
    dotnet = doctor()
    if options.doctor:
        return
    environment = dict(os.environ, DOTNET=dotnet)
    build_flags = ['-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false']
    gates = [
        ('Locked restore', [dotnet, 'restore', 'TestHarbor.sln', '--locked-mode', *build_flags]),
        ('Release build', [dotnet, 'build', 'TestHarbor.sln', '-c', 'Release', '--no-restore', *build_flags]),
        ('Tests and examples', [dotnet, 'test', 'TestHarbor.sln', '-c', 'Release', '--no-build',
                                '--no-restore', '--logger', 'trx', *build_flags]),
    ]
    for arguments, label in [(['.'], 'XML documentation audit'), (['--self-test'], 'XML audit negative tests')]:
        gates.append((label, [dotnet, 'run', '--project', 'tools/TestHarbor.DocCheck', '-c', 'Release',
                              '--no-build', '--no-restore', '--', *arguments]))
    for name in ('verify_failure_probes', 'verify_conformance', 'verify_discovery', 'verify_broker',
                 'verify_attachments', 'verify_cleanup_comparison', 'verify_injection', 'verify_generic_injection', 'build_docs'):
        gates.append((name, [sys.executable, str(ROOT / 'tools' / (name + '.py'))]))
    for name in ('TestHarbor', 'TestHarbor.NUnit', 'TestHarbor.Testing', 'TestHarbor.NUnit.Injection'):
        gates.append(('Package ' + name, [dotnet, 'pack', 'src/' + name, '-c', 'Release',
                                         '--no-build', '--no-restore', *build_flags]))
    gates.append(('Clean package consumer', [sys.executable, str(ROOT / 'tools/verify_compatibility.py')]))
    for label, command in gates:
        run(label, command, environment)
    print('\nPASS: all Release verification gates completed. Local packages have not been published.', flush=True)


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        raise SystemExit(130)
    except OSError as error:
        raise SystemExit(f'Cannot execute verification: {error}')
