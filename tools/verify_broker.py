#!/usr/bin/env python3
"""Verify broker exclusion, fencing, crash recovery and persistent quarantine using independent processes."""
import os
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
client = root / 'tools/TestHarbor.BrokerProbe/bin/Release/net10.0/TestHarbor.BrokerProbe.dll'
dotnet = os.environ.get('DOTNET', 'dotnet')


def run(directory, command, expected=0):
    """Run one isolated client and verify its exit status before returning its output."""
    result = subprocess.run([dotnet, str(client), directory, command], capture_output=True, text=True, timeout=15)
    assert result.returncode == expected, (command, result.returncode, result.stdout, result.stderr)
    return result.stdout.strip()


with tempfile.TemporaryDirectory(prefix='testharbor-broker-') as directory:
    first = int(run(directory, 'acquire'))
    holder = subprocess.Popen([dotnet, str(client), directory, 'hold'], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    try:
        token = int(holder.stdout.readline().strip())
        assert token > first
        run(directory, 'acquire', 2)
        # Kill without disposal: the OS releases the handle but the active record remains.
        holder.kill()
        holder.wait(timeout=10)
    finally:
        if holder.poll() is None:
            holder.kill()
            holder.wait(timeout=10)
    run(directory, 'acquire', 3)
    run(directory, 'recover')
    recovered = int(run(directory, 'acquire'))
    assert recovered > token
    run(directory, 'quarantine')
    run(directory, 'acquire', 3)
    run(directory, 'recover')
    assert int(run(directory, 'acquire')) > recovered
    run(directory, 'expire')
    run(directory, 'acquire', 3)
    run(directory, 'recover')
    run(directory, 'acquire')
    print('PASS: heartbeat-expiry quarantine without revocation; cross-process exclusion, increasing fencing, crash recovery and persistent quarantine.')
