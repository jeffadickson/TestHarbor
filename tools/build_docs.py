#!/usr/bin/env python3
"""Generate a standalone code reference from XML documentation for every authored C# project."""
from pathlib import Path
import html
import xml.etree.ElementTree as ET

def documentation_text(node):
    """Render XML prose while preserving inline cref and parameter references."""
    parts = [node.text or '']
    for child in node:
        reference = child.attrib.get('cref') or child.attrib.get('name') or child.attrib.get('langword')
        parts.append(documentation_text(child) or reference or '')
        parts.append(child.tail or '')
    return ''.join(parts)


root = Path(__file__).resolve().parents[1]
sections = []
projects = sorted(root.glob('src/*/*.csproj')) + sorted(root.glob('tests/*/*.csproj')) + sorted(root.glob('examples/*/*.csproj')) + sorted(root.glob('tools/*/*.csproj'))
for project in projects:
    assembly = project.stem
    source = project.parent / 'bin/Release/net10.0' / (assembly + '.xml')
    if not source.exists():
        raise SystemExit('Build Release first: dotnet build TestHarbor.sln -c Release')
    members = ET.parse(source).findall('./members/member')
    sections.append('<h2>' + assembly + '</h2>')
    for member in members:
        name = html.escape(member.attrib['name'])
        content = []
        for node in member:
            label = node.tag + ((' ' + (node.attrib.get('name') or node.attrib.get('cref'))) if ('name' in node.attrib or 'cref' in node.attrib) else '')
            text = ' '.join(documentation_text(node).split())
            content.append(f'<p><b>{html.escape(label)}:</b> {html.escape(text)}</p>')
        sections.append(f'<article><h3>{name}</h3>{"".join(content)}</article>')
page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>TestHarbor API · 0.3.0-alpha.1</title><style>
body{font:16px/1.6 system-ui,sans-serif;margin:0;background:#f3f5f8;color:#18243a}main{max-width:1000px;margin:auto;padding:40px 24px}h1{font-size:40px}h2{margin-top:50px;color:#155e75}h3{font:14px/1.6 ui-monospace,monospace;overflow-wrap:anywhere}article{background:white;border:1px solid #d8e0e9;border-radius:10px;padding:16px 24px;margin:18px 0}input{width:100%;box-sizing:border-box;padding:14px;font:inherit;border:1px solid #aac;border-radius:8px}small{color:#536079}</style>
<main><small>TESTHARBOR · 0.3.0-alpha.1</small><h1>Code documentation reference</h1>
<p>Source, tests, examples and tooling. Generated from compiler XML documentation, including private helpers.</p>
<input aria-label="Filter API members" placeholder="Filter members…" oninput="for(const a of document.querySelectorAll('article'))a.hidden=!a.textContent.toLowerCase().includes(this.value.toLowerCase())">
''' + ''.join(sections) + '</main></html>'
output = root / 'docs/api.html'
output.write_text(page, encoding="utf-8")
print(f'Generated {output} ({len(sections)-len(projects)} documented code entries)')
