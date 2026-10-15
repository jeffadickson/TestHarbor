# Developing TestHarbor

Install Git, a .NET 10 SDK compatible with `global.json`, and Python 3. The verification
scripts use only Python's standard library. Initial restore needs access to NuGet.org.
The project does not need Java or physical instruments; examples use simulated equipment.

```sh
git clone https://github.com/jeffadickson/TestHarbor.git
cd TestHarbor
python tools/verify.py --doctor
python tools/verify.py
```

On systems exposing Python as `python3`, substitute that command. The script resolves the
repository from its own location, so it also works when invoked by absolute path from
another directory. `DOTNET` accepts one executable path, including paths containing spaces;
it is not a shell command or a place to append arguments.

For example, in PowerShell:

```powershell
$env:DOTNET = 'C:\Program Files\dotnet\dotnet.exe'
python tools/verify.py
```

In a POSIX shell:

```sh
DOTNET=/opt/dotnet/dotnet python3 tools/verify.py
```

## Daily development

The complete command runs the same gates as CI, stops on failure and uses serialized
MSBuild with shared compilation disabled. For a focused edit, these commands are useful:

```sh
dotnet restore TestHarbor.sln --locked-mode
dotnet build TestHarbor.sln -c Release --no-restore
dotnet test tests/TestHarbor.Tests -c Release --no-build --filter FullyQualifiedName~InfrastructureTests
dotnet test examples/TestHarbor.Example -c Release --no-build
dotnet run --project tools/TestHarbor.DocCheck -c Release --no-build -- .
```

Run the full verifier before submitting a change. All authored C# declarations, including
private helpers, tests and tools, require XML documentation under
[the documentation policy](docs/XML-DOCUMENTATION.md). Python helpers use module and function
docstrings. Keep NUnit integration within supported public APIs and document API changes
in [compatibility notes](docs/COMPATIBILITY.md).

Dependency changes must update the affected `packages.lock.json` files intentionally;
do not remove locked-mode validation to bypass a restore failure.

## Outputs and failures

- Normal test reports are under each test project's `TestResults` directory.
- Deliberate failure probes are selected explicitly and checked by Python verifiers.
  A failed NUnit invocation within a probe can be expected; the verifier's exit status
  determines whether its outcome and cleanup evidence meet the contract.
- Some verifiers use temporary directories and remove successful scratch evidence.
- Generated API documentation is `docs/api.html`; review any changes before committing.
- Local packages are under `src/<project>/bin/Release/`. The clean consumer verifies all
  three packages and their XML documentation. No package is published automatically.
- GitHub Actions uploads retained reports, packages and the API reference even when a
  gate fails. See the first failed gate's output before rerunning.

If SDK diagnosis fails, install a compatible .NET 10 SDK or correct `DOTNET`. If restore
fails, check NuGet connectivity and the lockfile error. The script does not install tools
or change global machine settings. Stop a local run with Ctrl+C.
