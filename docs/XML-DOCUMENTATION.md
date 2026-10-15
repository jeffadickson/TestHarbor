# XML documentation policy

“Xdoc” means C# `///` XML documentation comments. Apply it to every authored declaration in source, tests, examples and tools: types, constructors, methods, properties, fields, enum members, delegates and local functions, including private members. Accessors, lambdas and local variables are not separate declaration targets. Build output under `bin`/`obj` is excluded; checked-in generated C# is not exempt.

Every declaration needs a meaningful `<summary>`. Document every parameter and type parameter by its exact name. Non-void methods, including Task and ValueTask methods, need `<returns>`; properties need `<value>`. Describe ownership, ordering, cancellation, thread safety and failure behavior in `<remarks>` and `<exception>` where relevant. Do not use placeholder text merely to satisfy a check.

The compiler emits XML documentation for every C# project and treats warnings as errors. The Roslyn-based `TestHarbor.DocCheck` additionally checks private declarations, XML syntax, missing/unknown parameter names, returns and property values. Its negative self-tests verify that omissions fail. This is structural enforcement; human review still checks accuracy and clarity, including exception semantics.

```sh
dotnet build TestHarbor.sln -c Release
dotnet run --project tools/TestHarbor.DocCheck -c Release --no-build -- .
dotnet run --project tools/TestHarbor.DocCheck -c Release --no-build -- --self-test
python tools/build_docs.py
python tools/build_gap_docs.py
```

`docs/api.html` includes all C# projects and private implementation documentation; it is a code reference, not a promise that private APIs are supported. Library packages include compiler XML files. Python tools use module/function docstrings, Markdown explains requirements and architecture, and workflow steps remain valid YAML. XML syntax is not inserted into languages that do not support C# documentation comments. C# snippets in documentation follow the same commenting convention as runnable examples.
