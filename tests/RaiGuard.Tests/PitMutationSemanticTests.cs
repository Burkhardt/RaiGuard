using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RaiGuard.Core.Rules;

namespace RaiGuard.Tests;

public class PitMutationSemanticTests
{
	private static readonly string[] AssemblyPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
		.Concat(new[] { typeof(JsonPit.Pit).Assembly.Location, typeof(Newtonsoft.Json.Linq.JObject).Assembly.Location, typeof(OsLib.RaiPath).Assembly.Location })
		.Distinct().ToArray();
	private static List<SyntaxDiagnosticFinding> Analyze(string body)
	{
		var source = """
using System;
using System.Collections.Generic;
using JsonPit;
using Newtonsoft.Json.Linq;
class Example {
 static PitItem Fetch(Pit source, string key) => source[key];
 static PitItem Detached(Pit source, string key) => source.GetAt(key, DateTimeOffset.UtcNow);
 static void Fill(Pit source, List<PitItem> target) { target.Add(source.GetAt("x", DateTimeOffset.UtcNow)); }
 static void Consume(object value) { }
 void Run(Pit pit, string k, string id, JObject p, List<PitItem> generated, JObject i, bool condition) {
""" + body + "\n} }";
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create("Probe", new[] { tree }, AssemblyPaths.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		return RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(tree, includeSystemIoAndFormatting: false, semanticModel: compilation.GetSemanticModel(tree))
			.Where(f => f.RuleId is "RAI011" or "RAI012").ToList();
	}

	public static IEnumerable<object[]> SuppliedProbes()
	{
		var cases = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/cr077-sipho-probes.json")))!;
		return cases.Select(pair => new object[] { pair.Key, pair.Value });
	}

	[Theory]
	[MemberData(nameof(SuppliedProbes))]
	public void SiphoShapesRespectLiveContract(string name, string body) { Assert.False(string.IsNullOrEmpty(name)); Assert.Empty(Analyze(body)); }

	[Theory]
	[InlineData("var value = pit.GetAt(k, DateTimeOffset.UtcNow); var alias = value; pit.Add((PitItem)alias!);")]
	[InlineData("pit.Add(Detached(pit, k));")]
	[InlineData("var value = pit.GetAt(k, DateTimeOffset.UtcNow); pit.Add(value); value = new PitItem(id);")]
	[InlineData("var loaded = new List<PitItem> { pit.GetAt(k, DateTimeOffset.UtcNow) }; foreach(var value in loaded) pit.Add(value);")]
	[InlineData("var value = pit[k]; var copy = new PitItem((JObject)value.DeepClone()); pit.Add(copy);")]
	[InlineData("var value = pit[k]; pit.Delete(k); pit.Add(value);")]
	[InlineData("var values = new List<PitItem>(); Fill(pit, values); foreach(var value in values) pit.Add(value);")]
	public void ProvenDetachedOrRetiredWritesAreReported(string body)
	{
		var finding = Assert.Single(Analyze(body)); Assert.Equal("RAI011", finding.RuleId);
	}

	[Theory]
	[InlineData("var value = pit.GetAt(k, DateTimeOffset.UtcNow); value = new PitItem(id); pit.Add(value);")]
	[InlineData("var value = new PitItem(id); pit.Add(value); value = pit.GetAt(k, DateTimeOffset.UtcNow);")]
	[InlineData("var value = new PitItem(id); if(condition) value = pit.GetAt(k, DateTimeOffset.UtcNow); pit.Add(value);")]
	[InlineData("var resultPit = new List<PitItem>(); var value = pit.GetAt(k, DateTimeOffset.UtcNow); resultPit.Add(value);")]
	[InlineData("var value = pit[k]; value.Remove(\"Namespace\"); pit.Add(value);")]
	public void ChronologyUncertaintyAndCollectionsDoNotProduceFalsePositives(string body) => Assert.Empty(Analyze(body));

	[Theory]
	[InlineData("var item = pit[k]; item[\"Modified\"] = DateTimeOffset.UtcNow;")]
	[InlineData("var item = pit.Get(k); item[\"Deleted\"] = false;")]
	[InlineData("var item = pit[k]; item.ExtendWith(new JObject { [\"Role\"] = \"Sax\", [\"Modified\"] = \"bad\" });")]
	[InlineData("var item = pit[k]; item.DeleteProperty(\"Modified\");")]
	[InlineData("var payload = new JObject { [\"Modified\"] = \"bad\" }; var item = pit[k]; item.ExtendWith(payload);")]
	public void ProtectedMutationsUseResolvedItemProvenance(string body)
	{ Assert.Equal("RAI012", Assert.Single(Analyze(body)).RuleId); }
}
