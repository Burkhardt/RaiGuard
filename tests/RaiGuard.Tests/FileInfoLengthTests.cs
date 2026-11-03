using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RaiGuard.CodeFixes.Fixes;
using RaiGuard.Core.Rules;

namespace RaiGuard.Tests;

public sealed class FileInfoLengthTests
{
	private const string Contracts = """
namespace OsLib
{
    public class RaiFile
    {
        public RaiFile(string filename) { }
        public string FullName => "video.mov";
        public long Length => 123;
    }
    public class ImageFile : RaiFile { public ImageFile(string name) : base(name) { } }
}
""";

	[Theory]
	[InlineData("new System.IO.FileInfo(path).Length", "new global::OsLib.RaiFile(path).Length")]
	[InlineData("new System.IO.FileInfo(fileName: path).Length", "new global::OsLib.RaiFile(path).Length")]
	[InlineData("(new System.IO.FileInfo(path)).Length", "new global::OsLib.RaiFile(path).Length")]
	[InlineData("new System.IO.FileInfo(master.FullName).Length", "master.Length")]
	[InlineData("new System.IO.FileInfo((master.FullName)).Length", "master.Length")]
	[InlineData("new System.IO.FileInfo(image.FullName).Length", "image.Length")]
	[InlineData("new FI(path).Length", "new global::OsLib.RaiFile(path).Length")]
	public async Task InlineLengthHasOneActionableWarningAndCompilableFix(string expression, string replacement)
	{
		var (text, diagnostics, actions) = await Fix(expression);
		var diagnostic = Assert.Single(diagnostics);
		Assert.Equal("RAI002", diagnostic.Id);
		Assert.Contains("RaiFile.Length", diagnostic.GetMessage());
		Assert.Contains("size is in bytes", diagnostic.GetMessage());
		Assert.DoesNotContain("System.IO.File.FileInfo", diagnostic.GetMessage());
		Assert.Equal(1, actions);
		Assert.Contains("return " + replacement + ";", text);
	}

	[Fact]
	public async Task ExistingFileInfoPropertyGetsSizeGuidanceWithoutAnUnsafeRewrite()
	{
		var (_, diagnostics, actions) = await Fix("info.Length");
		Assert.Contains("RaiFile.Length", Assert.Single(diagnostics).GetMessage());
		Assert.Equal(0, actions);
	}

	[Fact]
	public async Task OlderRaiFileContractGetsGuidanceButNoBrokenFix()
	{
		var (_, diagnostics, actions) = await Fix("new System.IO.FileInfo(path).Length", Contracts.Replace("public long Length => 123;", ""));
		Assert.Contains("RaiFile.Length", Assert.Single(diagnostics).GetMessage());
		Assert.Equal(0, actions);
	}

	[Fact]
	public async Task MissingOsLibGetsGuidanceWithoutIntroducingAnUnresolvedType()
	{
		var (_, diagnostics, actions) = await Fix("new System.IO.FileInfo(path).Length", "", includeFrameworkParameters: false);
		Assert.Contains("RaiFile.Length", Assert.Single(diagnostics).GetMessage());
		Assert.Equal(0, actions);
	}

	[Fact]
	public async Task UnrelatedFileInfoAndTypedRaiFileHaveNoWarnings()
	{
		var (_, diagnostics, actions) = await Fix("new FileInfo().Length + master.Length",
			Contracts + "\nclass FileInfo { public long Length => 1; }");
		Assert.Empty(diagnostics);
		Assert.Equal(0, actions);
	}

	[Fact]
	public async Task UnrelatedFullNameDoesNotGetCollapsedToLength()
	{
		var (text, diagnostics, actions) = await Fix("new System.IO.FileInfo(other.FullName).Length",
			Contracts + "\nclass Other { public string FullName => \"other.mov\"; public long Length => 42; }");
		Assert.Single(diagnostics);
		Assert.Equal(1, actions);
		Assert.Contains("new global::OsLib.RaiFile(other.FullName).Length", text);
	}

	[Theory]
	[InlineData("new System.IO.FileInfo(path).Length")]
	[InlineData("(new FileInfo(path)).Length")]
	public void RailSyntaxCheckAlsoSuggestsRaiFileLength(string expression)
	{
		var findings = RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(
			CSharpSyntaxTree.ParseText("class Example { long Read(string path) => " + expression + "; }"), ruleFilter: "RAI002");
		Assert.Contains("RaiFile.Length", Assert.Single(findings).Message);
	}

	private static async Task<(string Text, ImmutableArray<Diagnostic> Diagnostics, int Actions)> Fix(
		string expression, string contracts = Contracts, bool includeFrameworkParameters = true)
	{
		using var workspace = new AdhocWorkspace();
		var project = workspace.AddProject("Example", LanguageNames.CSharp)
			.WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
			.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
		Assert.True(workspace.TryApplyChanges(project.Solution));
		var parameters = includeFrameworkParameters ? ", OsLib.RaiFile master, OsLib.ImageFile image" : "";
		var source = "using FI = System.IO.FileInfo;\n" + contracts
			+ "\nclass Caller { long Read(string path, System.IO.FileInfo info, Other other" + parameters + ") { return " + expression + "; } }";
		if (!contracts.Contains("class Other")) source += "\nclass Other { }";
		var document = workspace.AddDocument(project.Id, "Example.cs", SourceText.From(source));
		var compilation = (await document.Project.GetCompilationAsync())!;
		Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SystemIoFileAnalyzer())).GetAnalyzerDiagnosticsAsync();
		var actions = new List<CodeAction>();
		foreach (var diagnostic in diagnostics)
		{
			var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
			await new SystemIoFileCodeFix().RegisterCodeFixesAsync(context);
		}
		if (actions.Count == 0) return (source, diagnostics, 0);
		var operation = (await actions.Single().GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
		var fixedDocument = operation.ChangedSolution.GetDocument(document.Id)!;
		var fixedCompilation = (await fixedDocument.Project.GetCompilationAsync())!;
		Assert.Empty(fixedCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		var remaining = await fixedCompilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SystemIoFileAnalyzer())).GetAnalyzerDiagnosticsAsync();
		Assert.Empty(remaining);
		return ((await fixedDocument.GetTextAsync()).ToString(), diagnostics, actions.Count);
	}
}
