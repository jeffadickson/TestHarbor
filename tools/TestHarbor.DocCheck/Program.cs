using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TestHarbor.DocCheck;

/// <summary>Audits XML documentation on every authored C# declaration, including private helpers.</summary>
internal static class Program
{
    /// <summary>Scans repository C# sources or runs deliberate positive and negative audit probes.</summary>
    /// <param name="args">Repository root, or --self-test; optional --inventory emits declaration JSON.</param>
    /// <returns>Zero for complete documentation; one for missing or invalid documentation.</returns>
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();
        var root = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? ".");
        var declarations = new List<Declaration>();
        foreach (var directory in new[] { "src", "tests", "examples", "tools" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")))
                declarations.AddRange(Inspect(Path.GetRelativePath(root, file), File.ReadAllText(file)));
        }
        if (args.Contains("--inventory"))
        { Console.WriteLine(JsonSerializer.Serialize(declarations)); return 0; }
        var failures = declarations.Where(x => x.Errors.Count != 0).ToArray();
        foreach (var item in failures)
            Console.Error.WriteLine($"{item.Path}:{item.Line}: {item.Name}: {string.Join("; ", item.Errors)}");
        Console.WriteLine($"XML documentation: {declarations.Count} declarations, {failures.Length} failures.");
        if (declarations.Count == 0) { Console.Error.WriteLine("No source declarations found."); return 1; }
        return failures.Length == 0 ? 0 : 1;
    }

    /// <summary>Parses C# with Roslyn and checks documentation structure against declaration signatures.</summary>
    /// <param name="path">Diagnostic path for the input source.</param>
    /// <param name="source">C# source text, including XML comments.</param>
    /// <returns>Declaration records with missing-tag or malformed-XML diagnostics.</returns>
    private static IEnumerable<Declaration> Inspect(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose));
        foreach (var node in tree.GetRoot().DescendantNodes().Where(IsDeclaration))
        {
            var parameters = node switch
            {
                BaseMethodDeclarationSyntax m => m.ParameterList.Parameters.Select(p => p.Identifier.ValueText),
                TypeDeclarationSyntax t => t.ParameterList?.Parameters.Select(p => p.Identifier.ValueText) ?? [],
                DelegateDeclarationSyntax d => d.ParameterList.Parameters.Select(p => p.Identifier.ValueText),
                IndexerDeclarationSyntax i => i.ParameterList.Parameters.Select(p => p.Identifier.ValueText),
                LocalFunctionStatementSyntax l => l.ParameterList.Parameters.Select(p => p.Identifier.ValueText),
                _ => []
            };
            var typeParameters = node switch
            {
                TypeDeclarationSyntax t => t.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText) ?? [],
                MethodDeclarationSyntax m => m.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText) ?? [],
                DelegateDeclarationSyntax d => d.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText) ?? [],
                LocalFunctionStatementSyntax l => l.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText) ?? [],
                _ => []
            };
            var returnType = node switch
            {
                MethodDeclarationSyntax m => m.ReturnType.ToString(),
                OperatorDeclarationSyntax o => o.ReturnType.ToString(),
                ConversionOperatorDeclarationSyntax c => c.Type.ToString(),
                DelegateDeclarationSyntax d => d.ReturnType.ToString(),
                LocalFunctionStatementSyntax l => l.ReturnType.ToString(),
                _ => "void"
            };
            var errors = new List<string>();
            var trivia = node.GetLeadingTrivia().Select(t => t.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().ToArray();
            XElement? doc = null;
            if (trivia.Length != 1) errors.Add("exactly one XML documentation block required");
            else
            {
                try
                {
                    var xml = Regex.Replace(trivia[0].ToFullString(), @"(?m)^\s*/// ?", "");
                    doc = XElement.Parse("<doc>" + xml + "</doc>");
                }
                catch (System.Xml.XmlException) { errors.Add("malformed XML documentation"); }
            }
            if (doc is not null)
            {
                CheckTag(doc, "summary", null, errors);
                foreach (var parameter in parameters) CheckTag(doc, "param", parameter, errors);
                foreach (var parameter in typeParameters) CheckTag(doc, "typeparam", parameter, errors);
                foreach (var parameter in doc.Elements("param"))
                    if (!parameters.Contains((string?)parameter.Attribute("name"))) errors.Add("unknown param name");
                foreach (var parameter in doc.Elements("typeparam"))
                    if (!typeParameters.Contains((string?)parameter.Attribute("name"))) errors.Add("unknown typeparam name");
                if (returnType != "void") CheckTag(doc, "returns", null, errors);
                if (node is BasePropertyDeclarationSyntax and not EventDeclarationSyntax) CheckTag(doc, "value", null, errors);
            }
            yield return new Declaration(path, tree.GetLineSpan(node.Span).StartLinePosition.Line + 1,
                node.SpanStart, Name(node), node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "",
                node.Kind().ToString(), parameters.ToArray(), typeParameters.ToArray(), returnType,
                node is BasePropertyDeclarationSyntax and not EventDeclarationSyntax, doc is not null, errors);
        }
    }

    /// <summary>Identifies authored members requiring documentation, excluding accessors and local variables.</summary>
    /// <param name="node">Syntax node to classify.</param>
    /// <returns>True for named types, members, delegates, enum values and local functions.</returns>
    private static bool IsDeclaration(SyntaxNode node) => node is BaseTypeDeclarationSyntax or BaseMethodDeclarationSyntax
        or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax or DelegateDeclarationSyntax
        or EnumMemberDeclarationSyntax or LocalFunctionStatementSyntax;

    /// <summary>Obtains a compact source-level name for an audited declaration.</summary>
    /// <param name="node">Supported declaration syntax.</param>
    /// <returns>The member identifier or operator/indexer description.</returns>
    private static string Name(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
        MethodDeclarationSyntax m => m.Identifier.ValueText,
        ConstructorDeclarationSyntax c => c.Identifier.ValueText,
        DestructorDeclarationSyntax d => "~" + d.Identifier.ValueText,
        PropertyDeclarationSyntax p => p.Identifier.ValueText,
        EventDeclarationSyntax e => e.Identifier.ValueText,
        BaseFieldDeclarationSyntax f => string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText)),
        EnumMemberDeclarationSyntax e => e.Identifier.ValueText,
        DelegateDeclarationSyntax d => d.Identifier.ValueText,
        LocalFunctionStatementSyntax l => l.Identifier.ValueText,
        _ => node.Kind().ToString()
    };

    /// <summary>Requires exactly one nonempty documentation tag for a declaration component.</summary>
    /// <param name="doc">Parsed documentation root.</param>
    /// <param name="tag">XML element name.</param>
    /// <param name="name">Optional parameter or type-parameter identifier.</param>
    /// <param name="errors">Diagnostics to append when validation fails.</param>
    private static void CheckTag(XElement doc, string tag, string? name, List<string> errors)
    {
        var elements = doc.Elements(tag).Where(x => name is null || (string?)x.Attribute("name") == name).ToArray();
        if (elements.Length != 1 || string.IsNullOrWhiteSpace(elements[0].Value))
            errors.Add($"one nonempty <{tag}{(name is null ? "" : " name=\"" + name + "\"")}> required");
    }

    /// <summary>Tests the audit against missing private comments, signatures, malformed XML and valid primary constructors.</summary>
    /// <returns>Zero when every deliberate documentation probe has the expected result.</returns>
    private static int SelfTest()
    {
        string[] invalid =
        [
            "class C { private int hidden; }",
            "/// <summary>Type.</summary>\nclass C {\n/// <summary>Method.</summary>\nprivate int M(int x) => x; }",
            "/// <summary>Broken\nclass C {}",
            "/// <summary>Type.</summary>\nclass C {\n/// <summary>Value.</summary>\npublic int P => 1; }",
            "/// <summary>Type.</summary>\n/// <param name=\"wrong\">Bad name.</param>\nclass C(int x) {}"
        ];
        const string valid = "/// <summary>A value holder.</summary>\n/// <param name=\"x\">Initial value.</param>\nclass C(int x) {\n/// <summary>Reads the value.</summary>\n/// <returns>The stored value.</returns>\nprivate int M() => x; }";
        if (invalid.Any(s => Inspect("probe.cs", s).All(d => d.Errors.Count == 0))
            || Inspect("valid.cs", valid).Any(d => d.Errors.Count != 0))
        { Console.Error.WriteLine("Documentation audit self-test failed."); return 1; }
        Console.WriteLine("Documentation audit self-test: five negative probes and one positive probe passed.");
        return 0;
    }

    /// <summary>A declaration signature and its documentation audit diagnostics.</summary>
    /// <param name="Path">Repository-relative source path.</param>
    /// <param name="Line">One-based source line.</param>
    /// <param name="Start">Source offset preceding declaration attributes.</param>
    /// <param name="Name">Declaration name.</param>
    /// <param name="Container">Enclosing type name, if present.</param>
    /// <param name="Kind">Roslyn syntax kind.</param>
    /// <param name="Parameters">Declared parameter names.</param>
    /// <param name="TypeParameters">Declared generic parameter names.</param>
    /// <param name="ReturnType">Return type text or void.</param>
    /// <param name="Property">Whether a value tag is required.</param>
    /// <param name="HasDoc">Whether a parseable XML documentation block exists.</param>
    /// <param name="Errors">Documentation diagnostics.</param>
    private sealed record Declaration(string Path, int Line, int Start, string Name, string Container,
        string Kind, string[] Parameters, string[] TypeParameters, string ReturnType, bool Property,
        bool HasDoc, List<string> Errors);
}
