using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RaiGuard.CodeFixes.Fixes;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Tests;

public sealed class RAI006CodeFixTests
{
	private const string Contracts = """
namespace OsLib
{
    public class RaiSystemResult { }
    public class RaiSystem
    {
        public RaiSystem(string executable, string arguments) { }
        public RaiSystem(string executable, string[] arguments) { }
        public RaiSystemResult ExecResult() => new RaiSystemResult();
    }
    public class RaiFile { public RaiFile(string filename) { } }
    public class RaiPath { public RaiPath(string directory) { } }
    public record PitsCommandOptions
    {
        public string Tenant { get; init; }
        public RaiPath PitRoot { get; init; }
        public string CloudProvider { get; init; }
        public bool NoLogo { get; init; }
        public bool Debug { get; init; }
        public bool RetainWindow { get; init; }
    }
    public record PitsSeedRequest
    {
        public static PitsSeedRequest ForPit(string name, RaiFile source) => new PitsSeedRequest();
        public PitsCommandOptions Options { get; init; }
        public bool RequireExisting { get; init; }
    }
    public class PitsCommand { public RaiSystemResult Seed(PitsSeedRequest request) => new RaiSystemResult(); }
}
""";

	[Fact]
	public async Task ConstantTenantSeedBecomesCompilableTypedRequest()
	{
		var (text, count) = await Fix("return new RaiSystem(\"pits\", \"seed Person --source /data/people.json -r SampleTenant -c OneDrive -n --patch\").ExecResult();");
		Assert.Equal(1, count);
		Assert.Contains("PitsSeedRequest.ForPit(\"Person\"", text);
		Assert.Contains("Tenant = \"SampleTenant\"", text);
		Assert.Contains("CloudProvider = \"OneDrive\"", text);
		Assert.Contains("NoLogo = true", text);
		Assert.Contains("RequireExisting = true", text);
		Assert.DoesNotContain("new RaiSystem(", text);
	}

	[Fact]
	public async Task TokenizedSourceWithSpacesIsPreservedWithoutGuessingQuoting()
	{
		var (text, count) = await Fix("return new RaiSystem(\"pits\", new[] { \"seed\", \"Person\", \"--source\", \"/data/source files/people.json\", \"-r\", \"SampleTenant\", \"--cloud\", \"OneDrive\" }).ExecResult();");
		Assert.Equal(1, count);
		Assert.Contains("RaiFile(\"/data/source files/people.json\")", text);
	}

	[Fact]
	public async Task ExplicitLocalRootUsesRaiPathAndExecutableConstantsAreSupported()
	{
		var (text, count) = await Fix("const string PitsExecutable = \"pits\"; return new RaiSystem(PitsExecutable, \"seed Person --source /data/people.json -r /data/pits/\").ExecResult();");
		Assert.Equal(1, count);
		Assert.Contains("PitRoot = new global::OsLib.RaiPath(\"/data/pits/\")", text);
	}

	[Theory]
	[InlineData("return new RaiSystem(\"pits\", args).ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", $\"seed Person --source {args} -r SampleTenant -c OneDrive\").ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", \"list -r SampleTenant\").ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", \"seed Person --source - -r SampleTenant -c OneDrive\").ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", \"seed Person --source /data/people.json -r SampleTenant\").ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", \"seed Person --source /data/people.json --unknown\").ExecResult();")]
	[InlineData("return new RaiSystem(\"pits\", \"seed Person --source '/data/source files/people.json'\").ExecResult();")]
	[InlineData("var process = new RaiSystem(\"pits\", \"seed Person --source /data/people.json\"); return process.ExecResult();")]
	public async Task UnrecoverableOrNonSeedCallsHaveNoAutomaticRewrite(string body)
	{
		var (_, count) = await Fix(body);
		Assert.Equal(0, count);
	}

	[Fact]
	public void DiagnosticGuidanceIncludesTenantFactoryAndDocumentationLink()
	{
		var descriptor = DiagnosticDescriptors.ProhibitUntypedProcessExecution;
		Assert.Contains("PitsSeedRequest.ForPit", descriptor.Description.ToString());
		Assert.Contains("Tenant = tenantName", descriptor.Description.ToString());
		Assert.Contains("CR069", descriptor.HelpLinkUri);
	}

	[Fact]
	public async Task OlderOsLibContractRequiresPackageUpgradeInsteadOfAnInvalidFix()
	{
		var (_, count) = await Fix("return new RaiSystem(\"pits\", \"seed Person --source /data/people.json -r SampleTenant -c OneDrive\").ExecResult();", olderContract: true);
		Assert.Equal(0, count);
	}

	private static async Task<(string Text, int Count)> Fix(string body, bool olderContract = false)
	{
		using var workspace = new AdhocWorkspace();
		var project = workspace.AddProject("Example", LanguageNames.CSharp)
			.WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
			.WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
		Assert.True(workspace.TryApplyChanges(project.Solution));
		var contracts = olderContract ? Contracts.Replace("public string Tenant { get; init; }", "") : Contracts;
		var document = workspace.AddDocument(project.Id, "Example.cs", SourceText.From("using OsLib;\n" + contracts + "\nclass Caller { public RaiSystemResult Run(string args) { " + body + " } }"));
		var root = (await document.GetSyntaxRootAsync())!;
		var creation = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
			.First(node => node.Type.ToString() == "RaiSystem");
		var diagnostic = Diagnostic.Create(DiagnosticDescriptors.ProhibitUntypedProcessExecution, creation.GetLocation(), "new RaiSystem(...) ", "PitsCommand");
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
		await new RAI006CodeFixProvider().RegisterCodeFixesAsync(context);
		if (actions.Count == 0) return ((await document.GetTextAsync()).ToString(), 0);
		var operation = (await actions.Single().GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>().Single();
		var fixedDocument = operation.ChangedSolution.GetDocument(document.Id)!;
		var compilation = (await fixedDocument.Project.GetCompilationAsync())!;
		Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		return ((await fixedDocument.GetTextAsync()).ToString(), actions.Count);
	}
}
