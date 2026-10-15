#!/usr/bin/env python3
"""Verify retained evidence appears in both completed TRX and native NUnit XML reports."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix='testharbor-attachments-') as directory:
    folder = Path(directory)
    artifacts = folder / 'artifacts'
    env = dict(os.environ, TESTHARBOR_EVIDENCE_OUTPUT=str(artifacts))
    command = [env.get('DOTNET', 'dotnet'), 'test', str(root / 'tests/TestHarbor.Tests'), '-c', 'Release',
               '--no-build', '--no-restore', '-m:1', '-nodeReuse:false', '--filter', 'FullyQualifiedName~EvidenceReportingTests',
               '--results-directory', directory, '--logger', 'trx;LogFileName=evidence.trx', '--',
               'NUnit.TestOutputXml=' + str(folder / 'nunit')]
    run = subprocess.run(command, env=env, capture_output=True, text=True, timeout=90)
    assert run.returncode == 0, run.stdout + run.stderr
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    trx = ET.parse(folder / 'evidence.trx')
    cases = trx.findall('.//t:UnitTestResult', ns)
    assert len(cases) == 1 and cases[0].attrib['outcome'] == 'Passed'
    files = trx.findall('.//t:ResultFile', ns)
    assert len(files) == 4, ET.tostring(trx.getroot())
    reports = list((folder / 'nunit').glob('*.xml'))
    assert len(reports) == 1, reports
    attachments = ET.parse(reports[0]).findall('.//test-case/attachments/attachment/filePath')
    assert len(attachments) == 4
    for attachment in attachments:
        assert Path(attachment.text).exists(), attachment.text
    assert '[redacted]' in next(artifacts.glob('*-evidence.txt')).read_text()
    print('PASS: four retained evidence attachments in TRX and native NUnit XML.')
