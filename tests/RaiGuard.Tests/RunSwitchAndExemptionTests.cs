using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Rules;

namespace RaiGuard.Tests;

public class RunSwitchAndExemptionTests
{
	private static CSharpCompilation Compile(params string[] sources)
	{
		var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Concat(new[] { typeof(OsLib.Os).Assembly.Location, typeof(Newtonsoft.Json.Linq.JObject).Assembly.Location }).Distinct();
		var compilation = CSharpCompilation.Create("CR070", sources.Select(s => CSharpSyntaxTree.ParseText(s)),
			paths.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		return compilation;
	}

	private static List<string> Findings(CSharpCompilation compilation) => compilation.SyntaxTrees.SelectMany(tree =>
		RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(tree, ruleFilter: "RAI007,RAI015", includeSystemIoAndFormatting: false,
			semanticModel: compilation.GetSemanticModel(tree))).Select(f => f.RuleId).OrderBy(id => id).ToList();

	[Fact]
	public void ProcessSwitchApiIsSanctionedButRawEnvironmentReadsAndWritesRemainAudible()
	{
		var c = Compile("""
using System;
using OsLib;
class Example {
 void Run() { var optIn = Os.RunSwitch("AIA_LIVE_TENANT_SCAN"); var bad = Environment.GetEnvironmentVariable("CLOUD_ROOT"); Environment.SetEnvironmentVariable("CLOUD_ROOT", "bad"); }
}
""");
		Assert.Equal(new[] { "RAI007", "RAI007" }, Findings(c));
		var tree = c.SyntaxTrees.Single();
		var finding = RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(tree, "RAI007", semanticModel: c.GetSemanticModel(tree)).First();
		Assert.Contains("Os.RunSwitch", finding.Message);
		Assert.Contains("persistent machine/storage", finding.Message);
	}

	[Fact]
	public void MethodExemptionDoesNotSilenceNeighboringMethodOrOtherRule()
	{
		var c = Compile("""
using System;
using OsLib;
using Newtonsoft.Json.Linq;
class Example {
 [RaiRuleExemption("RAI015", "Default date coercion is the regression subject.")]
 void Subject() { var value = JObject.Parse("{}"); var bad = Environment.GetEnvironmentVariable("CLOUD_ROOT"); }
 void Neighbor() { var value = JObject.Parse("{}"); }
}
""");
		Assert.Equal(new[] { "RAI007", "RAI015" }, Findings(c));
	}

	[Theory]
	[InlineData("RAI007", "A different rule")]
	[InlineData("RAI015", " ")]
	[InlineData("", "No wildcard exemptions")]
	public void InvalidOrDifferentDeclarationsDoNotHideTheFinding(string id, string reason)
	{
		var c = Compile($$"""
using OsLib; using Newtonsoft.Json.Linq;
class Example {
 [RaiRuleExemption("{{id}}", "{{reason}}")]
 void Subject() { var value = JObject.Parse("{}"); }
}
""");
		Assert.Equal(new[] { "RAI015" }, Findings(c));
	}

	[Fact]
	public void ClassAndPartialDeclarationsResolveSemanticallyButDoNotExemptDerivedTypes()
	{
		var c = Compile("""
using E = OsLib.RaiRuleExemptionAttribute;
[E("RAI015", "Parser comparison fixture")]
partial class Fixture { }
""", """
using Newtonsoft.Json.Linq;
partial class Fixture { void First() { var x = JObject.Parse("{}"); } void Second() { var x = JObject.Parse("{}"); } }
class Derived : Fixture { void Unrelated() { var x = JObject.Parse("{}"); } }
""");
		Assert.Equal(new[] { "RAI015" }, Findings(c));
	}

	[Fact]
	public void UnrelatedSameNamedAttributeIsNotAnExemption()
	{
		var c = Compile("""
using System; using Newtonsoft.Json.Linq;
class RaiRuleExemptionAttribute : Attribute { public RaiRuleExemptionAttribute(string id, string reason) {} }
class Example { [RaiRuleExemption("RAI015", "unrelated")] void Subject() { var value = JObject.Parse("{}"); } }
""");
		Assert.Equal(new[] { "RAI015" }, Findings(c));
	}

	[Fact]
	public async Task CompilerSuppressorHonorsDeclarationsForSeparateSystemIoAnalyzers()
	{
		var c = Compile("""
using OsLib;
class Example {
 [RaiRuleExemption("RAI003", "System.IO.Path is intentionally the comparison subject.")]
 void Subject() { var p = System.IO.Path.Combine("a", "b"); }
 void Neighbor() { var p = System.IO.Path.Combine("a", "b"); }
}
""");
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SystemIoPathAnalyzer(), new RaiRuleExemptionSuppressor());
		var diagnostics = await c.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
		Assert.Single(diagnostics.Where(d => d.Id == "RAI003" && !d.IsSuppressed));
		Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
	}
}
