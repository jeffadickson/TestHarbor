#!/usr/bin/env python3
"""Check public-API boundaries and consume packaged libraries from a clean temporary project."""
import os
import re
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
# Only exact alias targets in these files may refer to NUnit implementation types.
injection_allowlist = {
    'HarborInjectedFixtureAttribute.cs': {'TestSuite', 'TestFixture', 'Builders.DefaultTestCaseBuilder'},
    'InjectedDescriptors.cs': {'TypeWrapper', 'MethodWrapper'},
    'InjectionRuntime.cs': {'Commands.TestCommand', 'Commands.DelegatingTestCommand', 'TestExecutionContext', 'TestResult'},
}


def boundary_allowed(path, text):
    """Allow exact reviewed aliases in the injection package; reject all other implementation references."""
    permitted = injection_allowlist.get(path.name, set()) if path.parent.name == 'TestHarbor.NUnit.Injection' else set()
    for line in text.splitlines():
        if 'NUnit.Framework.Internal' not in line:
            continue
        match = re.fullmatch(r'using \w+ = NUnit\.Framework\.Internal\.([\w.]+);', line)
        if match is None or match.group(1) not in permitted:
            return False
    return True


for source in (root / 'src').rglob('*.cs'):
    if 'obj' in source.parts or 'bin' in source.parts:
        continue
    assert boundary_allowed(source, source.read_text(encoding='utf-8')), source
probe = Path('src/TestHarbor.NUnit.Injection/InjectionRuntime.cs')
assert boundary_allowed(probe, 'using Command = NUnit.Framework.Internal.Commands.TestCommand;')
assert not boundary_allowed(probe, 'using NUnit.Framework.Internal;')
assert not boundary_allowed(probe, 'using Bad = NUnit.Framework.Internal.Execution.SimpleWorkItem;')
assert not boundary_allowed(Path('src/TestHarbor/InjectionRuntime.cs'), 'using Command = NUnit.Framework.Internal.Commands.TestCommand;')
packages = ('TestHarbor', 'TestHarbor.NUnit', 'TestHarbor.Testing', 'TestHarbor.NUnit.Injection')
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
        '<TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsTestProject>true</IsTestProject><GenerateProgramFile>false</GenerateProgramFile></PropertyGroup><ItemGroup>'
        '<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />'
        '<PackageReference Include="NUnit3TestAdapter" Version="6.1.0" />' + references + '</ItemGroup></Project>')
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
    (folder / 'InjectedConsumer.cs').write_text('''using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TestHarbor.NUnit.Injection;
/// <summary>Checks actual injection from installed packages in a clean NUnit consumer.</summary>
/// <param name="service">Constructor-injected scoped service.</param>
[HarborInjectedFixture(typeof(ConsumerProfile))]
public sealed class InjectedConsumer(ConsumerService service)
{
    /// <summary>Checks mixed binding and shared constructor/method scope identity.</summary>
    /// <param name="input">Ordinary case data.</param>
    /// <param name="same">Method-injected service.</param>
    [TestCase(42)]
    public void UsesInstalledPackage(int input, [FromServices] ConsumerService same)
    { Assert.That(input, Is.EqualTo(42)); Assert.That(same, Is.SameAs(service)); }
}
/// <summary>Defines the clean consumer's registration profile.</summary>
public sealed class ConsumerProfile : InjectionProfile
{
    /// <summary>Registers a scoped service.</summary>
    /// <param name="services">Attempt-local registration collection.</param>
    public override void Configure(IServiceCollection services) => services.AddScoped<ConsumerService>();
}
/// <summary>Provides a scope identity for package verification.</summary>
public sealed class ConsumerService;
''')
    # A config file preserves URI syntax across platform-specific CLI source parsing.
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    ET.ElementTree(config).write(folder / 'NuGet.Config', encoding='utf-8', xml_declaration=True)
    dotnet = os.environ.get('DOTNET', 'dotnet')
    for args in ([dotnet, 'restore', '--configfile', str(folder / 'NuGet.Config')],
                 [dotnet, 'run', '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false', '-m:1'],
                 [dotnet, 'test', '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~InjectedConsumer']):
        result = subprocess.run(args, cwd=folder, capture_output=True, text=True, timeout=180)
        assert result.returncode == 0, result.stdout + result.stderr
    print('PASS: scoped NUnit boundary allowlist, packaged XML docs and clean package consumer injection execution.')
