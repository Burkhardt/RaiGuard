using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RaiGuard.Core.Rules;

namespace RaiGuard.Tests;

/// <summary>RAI018-RAI020: flag/ticket lifecycle. Self-contained JsonPit stubs keep the tests independent of the package.</summary>
public class PitFlagLifecycleTests
{
	private static readonly string[] AssemblyPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(OsLib.LocalFileSystem.SearchPathSeparator);

	private const string Stubs = """
using System;
namespace JsonPit {
	public class MasterFlagFile { public MasterFlagFile(string dir, string name, string server = null) { } internal void WriteTicket(object ticket) { } public void Update() { } }
	public class ProcessFlagFile : MasterFlagFile { public ProcessFlagFile() : base("d", "n") { } public new void Update() { } }
	public class JsonPitBase {
		public static TimeSpan ChangeFileCleanupGrace { get; set; } = TimeSpan.FromMinutes(10);
		public ProcessFlagFile ProcessFlag() => new ProcessFlagFile();
		public MasterFlagFile MasterFlag() => new MasterFlagFile("d", "Master");
		public bool TryAcquireMaster() => ClaimAvailableTicket();
		private bool ClaimAvailableTicket() { MasterFlag().WriteTicket(null); ProcessFlag().Update(); return true; }
		protected bool ReturnClaim() { MasterFlag().WriteTicket(null); return true; }
	}
}
""";

	private static List<SyntaxDiagnosticFinding> Analyze(string pitBody, string consumerBody = "")
	{
		var source = Stubs + "\nnamespace JsonPit {\n public class Pit : JsonPitBase {\n" + pitBody + "\n}\n}\n" +
			"class Consumer { void Run(JsonPit.Pit pit) {\n" + consumerBody + "\n} }\n";
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create("Probe", new[] { tree },
			AssemblyPaths.Select(p => MetadataReference.CreateFromFile(p)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
		return RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(tree, includeSystemIoAndFormatting: false,
				semanticModel: compilation.GetSemanticModel(tree))
			.Where(f => f.RuleId is "RAI018" or "RAI019" or "RAI020").ToList();
	}

	[Fact]
	public void CleanSaveProtocol_IsNotReported()
	{
		Assert.Empty(Analyze("""
public void Save() { var ok = BeginSave(); if (ok) Store(); }
bool BeginSave() => TryAcquireMaster();
bool Store() => ReturnClaim();
"""));
	}

	[Fact]
	public void ReadPathThatClaimsTheTicket_ReportsRai018()
	{
		var finding = Assert.Single(Analyze("public void Load() { TryAcquireMaster(); }")
			.Where(f => f.RuleId == "RAI018"));
		Assert.Contains("Load", finding.Message);
	}

	[Fact]
	public void ConsumerWritingProcessFlag_ReportsRai018()
	{
		Assert.Single(Analyze("", "pit.ProcessFlag().Update();"), f => f.RuleId == "RAI018");
	}

	[Fact]
	public void ConsumerReadingProcessFlagObject_IsNotReported()
	{
		Assert.Empty(Analyze("", "var flag = pit.ProcessFlag();"));
	}

	[Fact]
	public void EntryPointThatClaimsButNeverReturns_ReportsRai019()
	{
		var findings = Analyze("public void Save() { BeginSave(); }\nbool BeginSave() => TryAcquireMaster();")
			.Where(f => f.RuleId == "RAI019").ToList();
		var finding = Assert.Single(findings);
		Assert.Contains("Save", finding.Message);
	}

	[Fact]
	public void MasterTicketUpdateOutsideClaimProtocol_ReportsRai019()
	{
		Assert.Single(Analyze("public void Hack() { MasterFlag().Update(); }"), f => f.RuleId == "RAI019");
	}

	[Fact]
	public void MasterFlagConstructedWithServer_ReportsRai019()
	{
		Assert.Single(Analyze("", "var f = new JsonPit.MasterFlagFile(\"d\", \"Master\", \"srv\");"), f => f.RuleId == "RAI019");
	}

	[Fact]
	public void CleanupGraceOverride_ReportsRai020()
	{
		var finding = Assert.Single(Analyze("public void Overrides() { JsonPitBase.ChangeFileCleanupGrace = TimeSpan.FromDays(30); }"),
			f => f.RuleId == "RAI020");
		Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, finding.Descriptor.DefaultSeverity);
	}

	[Fact]
	public void SuppressingCleanupByParameter_ReportsRai020()
	{
		Assert.Single(Analyze("public void Merge(bool cleanup = true) { }", "pit.Merge(cleanup: false);"), f => f.RuleId == "RAI020");
		Assert.Single(Analyze("public void Merge(bool keepChangeFiles = false) { }", "pit.Merge(true);"), f => f.RuleId == "RAI020");
	}

	[Fact]
	public void DefaultCleanup_IsNotReported()
	{
		Assert.Empty(Analyze("public void Merge(bool cleanup = true) { }", "pit.Merge(cleanup: true); pit.Merge();"));
	}

	[Fact]
	public void DescriptorSeverities_AreErrorForFlagRulesAndWarningForCleanup()
	{
		Assert.Equal(DiagnosticSeverity.Error, RaiGuard.Core.Diagnostics.DiagnosticDescriptors.ProhibitProcessFlagWriteOutsideWriteClaim.DefaultSeverity);
		Assert.Equal(DiagnosticSeverity.Error, RaiGuard.Core.Diagnostics.DiagnosticDescriptors.EnforceMasterTicketRelease.DefaultSeverity);
		Assert.Equal(DiagnosticSeverity.Warning, RaiGuard.Core.Diagnostics.DiagnosticDescriptors.WarnChangeFileCleanupAvoided.DefaultSeverity);
	}
}
