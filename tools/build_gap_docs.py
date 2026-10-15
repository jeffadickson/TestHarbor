#!/usr/bin/env python3
"""Render the reviewed capability matrix and linked requirements from their JSON source."""
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / 'docs/junit-gap-analysis.json').read_text())
LABELS = {'N': 'Native NUnit; reuse', 'P': 'Partial coverage; wrapper work', 'A': 'Existing advantage or domain enhancement', 'B': 'Wrapper boundary; feasibility required', 'H': 'TestHarbor delivered', 'X': 'Platform-specific or outside scope'}


def task(requirement):
    """Return the canonical GitHub task link for one requirement."""
    return f"[#{requirement['issue']}](https://github.com/jeffadickson/TestHarbor/issues/{requirement['issue']})"


def cell(value):
    """Escape Markdown table delimiters in a single-line cell."""
    return str(value).replace('|', '\\|').replace('\n', ' ')


lines = ['# JUnit / NUnit / TestHarbor gap analysis', '', f"Reviewed **{DATA['reviewed']}**. Baseline: **{DATA['baseline']}**.", '',
'This is a functional comparison of Java JUnit Jupiter/Platform with C# NUnit and the current TestHarbor wrapper. It covers 56 capabilities, not every overload or every third-party Java library. No percentage of compatibility is implied. Versioned official JUnit documentation, official NUnit documentation and local implementation evidence are linked for each row.', '',
'JUnit remains actively developed. Its modern extension model is substantially broader than the original JUnit class model. NUnit already supplies the core assertions, discovery, parameterized tests, fixture data, filtering and runner integration; TestHarbor should reuse these.', '',
'The highest-value work is explicit lifecycle contracts, conformance tests, owned resources, conditional execution, artifacts and broader service scopes. Transparent parameter injection, runtime-generated test trees and scheduler-level resource locks require feasibility decisions. Cross-process hardware coordination is a domain enhancement, not a missing JUnit parity feature.', '',
'## Scope and interpretation', '',
'“Partial” means related functionality exists but semantics differ. A task is not proof that complete equivalence can be implemented. The public-API-only constraint remains binding: no NUnit fork, internal execution commands, internal reflection or new runner is authorized by this analysis. JUnit experimental APIs are inspiration rather than a compatibility promise.', '',
'Existing StartAsync/StopAsync hooks surround the derived NUnit lifecycle; they are not all JUnit callbacks. Services access is explicit, not ParameterResolver injection. NUnit TestCaseSource is discovery-time data, not an execution-time TestFactory. Base teardown observes a provisional outcome, not a guaranteed final runner result. Setup-time resource waits do not change NUnit scheduling.', '',
'## Classification', '', '| Class | Meaning | Capabilities |', '|---|---|---:|']
counts = Counter(f['status'] for f in DATA['features'])
lines += [f'| {key} | {label} | {counts[key]} |' for key, label in LABELS.items()]
for category in dict.fromkeys(f['category'] for f in DATA['features']):
    lines += ['', f'## {category}', '', '| ID / capability | JUnit | NUnit + current TestHarbor | Class | Decision / requirements | Evidence |', '|---|---|---|---|---|---|']
    for f in DATA['features']:
        if f['category'] != category:
            continue
        requirements = ', '.join(f"[{r}](REQUIREMENTS-v0.3.md#{r.lower()})" if r.startswith('JG-') else r for r in f['requirements'])
        sources = ' '.join(f"[{s}]({DATA['sources'][s]})" for s in f['sources'])
        lines.append('| ' + ' | '.join(map(cell, [f['id'] + ' ' + f['feature'], f['junit'], f['current'], f['status'], requirements, sources])) + ' |')
lines += ['', '## Implementation order', '',
'1. JG-016 XML documentation enforcement (delivered in this change).',
'2. JG-012 conformance kit, then JG-014 compatibility policy and JG-004 lifecycle contracts; JG-001 declarations can proceed independently.',
'3. JG-002 scopes, JG-005 conditions, JG-010 budgets and JG-011 artifacts; JG-006 ownership follows outcome/artifact contracts.',
'4. JG-003 profiles, JG-007 typed templates, JG-008 injection feasibility and JG-009 contexts.',
'5. JG-013 runtime discovery feasibility and JG-015 cross-process broker only after their prerequisites.', '',
'## Maintenance', '',
'Edit `junit-gap-analysis.json`, then run `python tools/build_gap_docs.py`. Requirement acceptance criteria and task links are generated from that same source. Re-review official versioned documentation when changing the NUnit/JUnit baseline. Status reflects implementation, not merely the existence of a task.', '']
(ROOT / 'docs/JUNIT-GAP-ANALYSIS.md').write_text('\n'.join(lines))
lines = ['# TestHarbor next-release requirements and tasks', '',
'These requirements follow the [56-capability analysis](JUNIT-GAP-ANALYSIS.md). “v0.3” names the planning document, not a claim that all work is implemented. Existing v0.1/v0.2 requirements remain applicable. P0 is the current documentation gate; P1 is foundational delivery; P2 is ergonomics; P3 is later feasibility or distributed infrastructure.', '',
'## Task index', '', '| Requirement | Priority | Task | Status |', '|---|---|---|---|']
for r in DATA['requirements']:
    lines.append(f"| [{r['id']} {r['title']}](#{r['id'].lower()}) | {r['priority']} | {task(r)} | {r['status']} |")
for r in DATA['requirements']:
    lines += ['', f"<a id=\"{r['id'].lower()}\"></a>", f"## {r['id']} — {r['title']}", '', f"{r['priority']} · {task(r)} · {r['status']}", '', r['goal'], '', 'Acceptance criteria:', '']
    lines += [f'- {a}' for a in r['acceptance']]
    lines += ['', 'Dependencies: ' + (', '.join(r['dependencies']) or 'none') + '.', '',
              'Verification task: add focused unit tests plus real-runner probes where discovery, outcomes or lifecycle behavior is involved. Include XML documentation and runnable examples for every new API; CI must pass on Linux, Windows and macOS.']
lines += ['', '## Definition of done', '',
'Each task requires a public-API-only design, explicit ownership/cancellation/error semantics, acceptance evidence, XML documentation for every authored C# declaration, generated reference updates and a green three-platform CI run. Feasibility tasks may finish with a documented no-go and supported alternative; they must not quietly introduce NUnit internals.', '']
(ROOT / 'docs/REQUIREMENTS-v0.3.md').write_text('\n'.join(lines))
print(f"Rendered {len(DATA['features'])} capabilities and {len(DATA['requirements'])} requirements.")
