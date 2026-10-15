#!/usr/bin/env python3
"""Run deliberate NUnit failures and verify cleanup evidence and exact expected outcomes."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix="testharbor-probes-") as directory:
    env = dict(os.environ, TESTHARBOR_PROBE_OUTPUT=directory)
    command = [env.get("DOTNET", "dotnet"), "test", str(root / "tests/TestHarbor.Tests"),
               "-c", "Release", "--no-build", "--no-restore", "-m:1", "-nodeReuse:false",
               "--filter", "FullyQualifiedName~LifecycleFailureProbes", "--results-directory", directory,
               "--logger", "trx;LogFileName=probes.trx"]
    result = subprocess.run(command, env=env, capture_output=True, text=True)
    report = Path(directory) / "probes.trx"
    if result.returncode != 1 or not report.exists():
        raise SystemExit("Unexpected runner result:\n" + result.stdout + result.stderr)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    outcomes = ET.parse(report).findall(".//t:UnitTestResult", ns)
    assert len(outcomes) == 4, result.stdout
    for mode in ("setup", "body", "teardown", "cleanup"):
        outcome = next(x for x in outcomes if f'("{mode}")' in x.attrib["testName"])
        assert outcome.attrib["outcome"] == "Failed", ET.tostring(outcome)
        assert "intentional-" + mode in "".join(outcome.itertext()), ET.tostring(outcome)
        assert (Path(directory) / (mode + ".txt")).read_text() == "cleanup-held-lease"
    assert (Path(directory) / "released.txt").read_text() == "released"
    print("PASS: four intentional lifecycle failures; cleanup retained leases; teardown/cleanup failures quarantined resources; explicit recovery succeeded.")
