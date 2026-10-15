#!/usr/bin/env python3
"""Check public-API boundaries and consume packaged libraries from a clean temporary project."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
for source in (root / 'src').rglob('*.cs'):
    if 'obj' in source.parts or 'bin' in source.parts:
        continue
    assert 'NUnit.Framework.Internal' not in source.read_text(), source
packages = ('TestHarbor', 'TestHarbor.NUnit', 'TestHarbor.Testing')
with tempfile.TemporaryDirectory(prefix='testharbor-consumer-') as directory:
    folder = Path(directory)
    feed = folder / 'feed'
    feed.mkdir()
    for name in packages:
        package = root / f'src/{name}/bin/Release/{name}.{version}.nupkg'
        with zipfile.ZipFile(package) as archive:
            assert any(path.endswith(f'/{name}.xml') for path in archive.namelist()), name
        (feed / package.name).write_bytes(package.read_bytes())
    references = ''.join(f'<PackageReference Include="{name}" Version="{version}" />' for name in packages)
    (folder / 'Consumer.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><GenerateDocumentationFile>true</GenerateDocumentationFile>'
        '<TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
    (folder / 'Program.cs').write_text('''using TestHarbor.Testing;
using TestHarbor.NUnit;
/// <summary>Exercises the packaged dependency graph without repository project references.</summary>
internal static class Program
{
    /// <summary>Runs a packaged session conformance probe.</summary>
    /// <returns>Zero when cleanup released the package-created resource.</returns>
    private static async Task<int> Main()
    {
        _ = typeof(HarborFixture);
        var result = await new ExtensionProbe().RunAsync(_ => { });
        return result.LeaseReleased && result.Failure is null ? 0 : 1;
    }
}
''')
    dotnet = os.environ.get('DOTNET', 'dotnet')
    for args in ([dotnet, 'restore', '--source', str(feed), '--source', 'https://api.nuget.org/v3/index.json'],
                 [dotnet, 'run', '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false', '-m:1']):
        result = subprocess.run(args, cwd=folder, capture_output=True, text=True, timeout=180)
        assert result.returncode == 0, result.stdout + result.stderr
    print('PASS: public-API boundary, packaged XML docs and clean package consumer execution.')
