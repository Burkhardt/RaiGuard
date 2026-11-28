using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Rules;

namespace RaiGuard.Tests;

public sealed class TestConfigurationAnalysisTests
{
	private static CSharpCompilation Compile(string source, string path = "Sample.Tests/Example.cs")
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(OsLib.LocalFileSystem.SearchPathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path));
		var tree = CSharpSyntaxTree.ParseText(source, path: path);
		return CSharpCompilation.Create("ConfigurationTests", [tree], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
	}

	private static List<SyntaxDiagnosticFinding> Findings(CSharpCompilation compilation) =>
		compilation.SyntaxTrees.SelectMany(tree => RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(
			tree, "RAI021", semanticModel: compilation.GetSemanticModel(tree))).ToList();

	[Theory]
	[InlineData("Os.TestCloud", 0)]
	[InlineData("Os.LiveCloud", 0)]
	[InlineData("\"OneDrive\"", 1)]
	public void RAI021_AllowsExecCloudParam_WhenValueFromOsTestCloud(string value, int count)
	{
		foreach (var initializerType in new[] { "JObject", "System.Collections.Generic.Dictionary<string, object>" })
		{
			var compilation = Compile($$"""
using OsLib;
namespace OsLib { static class Os { public static string TestCloud => ""; public static string LiveCloud => ""; } }
class JObject : System.Collections.Generic.Dictionary<string, object> { }
class ExecTests { object Seed() => new {{initializerType}} { ["cloud"] = {{value}} }; }
""");
			Assert.Equal(count, Findings(compilation).Count);
		}
	}

	[Fact]
	public async Task DetectsConfigurationOverridesInCliAndCompiler()
	{
		var compilation = Compile("""
using System;
using System.Collections.Generic;
using System.IO;
class TestRuntime { public static void Create(string home) {} }
class Example {
 static void SetEnvironmentVariable(string name, string value) {}
 void Test(Dictionary<string, string> env, string home) {
  const string key = "HOME";
  Environment.SetEnvironmentVariable(key, home);
  SetEnvironmentVariable("USERPROFILE", home);
  SetEnvironmentVariable("OneDrive", home);
  env["RAIKEEP_CONFIG"] = home;
  TestRuntime.Create(home);
  var path = Path.Combine(home, ".config", "RAIkeep.json5");
  File.WriteAllText(path, "{}");
 }
}
""");
		var findings = Findings(compilation);
		Assert.True(findings.Count == 6, string.Join("\n", findings.Select(finding => finding.Message)));
		Assert.Contains(findings, finding => finding.Message.Contains("HOME"));
		Assert.Contains(findings, finding => finding.Message.Contains("USERPROFILE"));
		Assert.Contains(findings, finding => finding.Message.Contains("OneDrive"));
		Assert.Contains(findings, finding => finding.Message.Contains("RAIKEEP_CONFIG"));
		Assert.Contains(findings, finding => finding.Message.Contains("TestRuntime"));
		Assert.Contains(findings, finding => finding.Message.Contains("configuration file"));
		var diagnostics = await compilation.WithAnalyzers(
			ImmutableArray.Create<DiagnosticAnalyzer>(new RaiKeepArchitectureAnalyzer())).GetAnalyzerDiagnosticsAsync();
		Assert.Equal(6, diagnostics.Count(diagnostic => diagnostic.Id == "RAI021"));
		Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
	}

	[Fact]
	public void DetectsDirectAndAliasedLoadedConfigurationMutation()
	{
		var compilation = Compile("""
class Os { public static dynamic Config => null; }
class Example {
 void Test() {
  Os.Config.Cloud["Dropbox"] = "/other";
  var settings = Os.Config;
  settings["TestCloud"] = "OneDrive";
  settings = null;
 }
}
""");
		Assert.Equal(2, Findings(compilation).Count);
	}

	[Fact]
	public void ReportsSyntheticCloudConfigurationForReview()
	{
		var compilation = Compile("""
class ConfigurationDocument { public ConfigurationDocument(string text) {} }
class TestJson { public static object ParseObject(string text) => null; }
class Example {
 void Test() {
  var a = new ConfigurationDocument("{Cloud:{Dropbox:'/tmp'}}");
  var b = TestJson.ParseObject("{TestCloud:'Dropbox'}");
  var c = TestJson.ParseObject("{Name:'value'}");
 }
}
""");
		Assert.Equal(2, Findings(compilation).Count);
	}

	[Fact]
	public void AllowsReadsIndependentTestDataAndProductionCode()
	{
		const string source = """
using System;
using System.IO;
class Example {
 void Test(string home) {
  var path = Path.Combine(home, ".config", "RAIkeep.json5");
  File.ReadAllText(path);
  File.WriteAllText(Path.Combine(home, "other.json"), "{}");
  Environment.SetEnvironmentVariable("TERM", "dumb");
 }
}
""";
		Assert.Empty(Findings(Compile(source)));
		Assert.Empty(Findings(Compile("""
using System;
class Example { void Run() { Environment.SetEnvironmentVariable("HOME", "example"); } }
""", "src/Example.cs")));
	}
}
