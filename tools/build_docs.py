#!/usr/bin/env python3
"""Generate a standalone public API reference from compiler XML documentation."""
from pathlib import Path
import html
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
sections = []
for assembly in ('TestHarbor', 'TestHarbor.NUnit'):
    source = root / 'src' / assembly / 'bin/Release/net10.0' / (assembly + '.xml')
    if not source.exists():
        raise SystemExit('Build Release first: dotnet build TestHarbor.sln -c Release')
    members = ET.parse(source).findall('./members/member')
    sections.append('<h2>' + assembly + '</h2>')
    for member in members:
        name = html.escape(member.attrib['name'])
        content = []
        for node in member:
            label = node.tag + ((' ' + node.attrib['name']) if 'name' in node.attrib else '')
            text = ' '.join(''.join(node.itertext()).split())
            content.append(f'<p><b>{html.escape(label)}:</b> {html.escape(text)}</p>')
        sections.append(f'<article><h3>{name}</h3>{"".join(content)}</article>')
page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>TestHarbor API · 0.2.0-alpha.1</title><style>
body{font:16px/1.6 system-ui,sans-serif;margin:0;background:#f3f5f8;color:#18243a}main{max-width:1000px;margin:auto;padding:40px 24px}h1{font-size:40px}h2{margin-top:50px;color:#155e75}h3{font:14px/1.6 ui-monospace,monospace;overflow-wrap:anywhere}article{background:white;border:1px solid #d8e0e9;border-radius:10px;padding:16px 24px;margin:18px 0}input{width:100%;box-sizing:border-box;padding:14px;font:inherit;border:1px solid #aac;border-radius:8px}small{color:#536079}</style>
<main><small>TESTHARBOR · 0.2.0-alpha.1</small><h1>Public API reference</h1>
<p>A companion layer over NUnit. Generated from compiler XML documentation.</p>
<input aria-label="Filter API members" placeholder="Filter members…" oninput="for(const a of document.querySelectorAll('article'))a.hidden=!a.textContent.toLowerCase().includes(this.value.toLowerCase())">
''' + ''.join(sections) + '</main></html>'
output = root / 'docs/api.html'
output.write_text(page)
print(f'Generated {output} ({len(sections)-2} public API entries)')
