using System.Text.Json;

namespace RaiGuard.Tests;

// Keep CLI tests in one xUnit class: they redirect the process-wide console and
// some temporarily change the working directory. They must not run concurrently.
public partial class RailCliTests
{
	private const string CleanSource = "public class Clean { }\n";
	private const string BadSource = "class Bad { void M() { System.IO.Directory.GetFiles(\".\"); } }\n";

	[Fact]
	public void MultipleDirectories_FindingOnlyInFourth_IsNotDropped()
	{
		using var fixture = new MultiPathFixture();
		var paths = Enumerable.Range(1, 4).Select(i => fixture.Directory("Project" + i)).ToArray();
		for (var i = 0; i < paths.Length; i++) File.WriteAllText(Path.Combine(paths[i], "Source.cs"), i == 3 ? BadSource : CleanSource);
		var result = Invoke(["check", .. paths, "--rule", "RAI001", "-n"]);
		Assert.Equal(1, result.Exit);
		Assert.Contains(Path.Combine(paths[3], "Source.cs"), result.Output);
		Assert.Contains("across 4 file(s)", result.Output);
	}

	[Fact]
	public void CombinedCount_IsComplete_AndSecondDirectoryFindingChangesExitStatus()
	{
		using var fixture = new MultiPathFixture();
		var first = fixture.Source("first/A.cs", CleanSource);
		var second = fixture.Source("second/B.cs", CleanSource);
		var args = new[] { "check", Path.GetDirectoryName(first)!, Path.GetDirectoryName(second)!, "--rule", "RAI001", "-n" };
		var clean = Invoke(args);
		Assert.Equal(0, clean.Exit);
		Assert.Contains("Checked 2 files", clean.Output);
		File.WriteAllText(second, BadSource);
		var bad = Invoke(args);
		Assert.Equal(1, bad.Exit);
		Assert.Contains(second, bad.Output);
	}

	[Fact]
	public void OverlappingRootsAndFiles_AreDeduplicatedAndOrdered()
	{
		using var fixture = new MultiPathFixture();
		var z = fixture.Source("parent/nested/Z.cs", BadSource);
		var a = fixture.Source("parent/A.cs", BadSource);
		string[] inputs = [z, fixture.Root, Path.GetDirectoryName(z)!, a, a, Path.Combine(fixture.Root, "parent", "..", "parent")];
		var files = SourceSelection.Resolve(inputs, new SourceFileSystem());
		Assert.Equal(new[] { a, z }, files);
		var result = Invoke(["check", .. inputs, "--json", "--rule", "RAI001"]);
		Assert.Equal(1, result.Exit);
		using var json = JsonDocument.Parse(result.Output);
		Assert.Equal(new[] { a, z }, json.RootElement.EnumerateArray().Select(r => r.GetProperty("File").GetString()));
	}

	[Fact]
	public void MixedRepeatedPathOptionsAndFilters_PreserveEveryTarget()
	{
		using var fixture = new MultiPathFixture();
		var a = fixture.Source("one space/A.cs", BadSource);
		var b = fixture.Source("B.cs", BadSource);
		var c = fixture.Source("C.cs", BadSource);
		var result = Invoke(["check", a, "--rule", "RAI001", "-p", b, "--group", "OsLib", "--path", c, "--json"]);
		Assert.Equal(1, result.Exit);
		using var json = JsonDocument.Parse(result.Output);
		Assert.Equal(new[] { a, b, c }.Order(StringComparer.Ordinal), json.RootElement.EnumerateArray().Select(r => r.GetProperty("File").GetString()));
		var parsed = RailArguments.Parse(["check", a, "--rule", "RAI001", "--group", "OsLib", "-p", b, "--path", c]);
		Assert.Equal(new[] { a, b, c }, parsed.Paths);
		Assert.Equal("RAI001", parsed.Rule);
		Assert.Equal("OsLib", parsed.Group);
	}

	[Theory]
	[InlineData("-h")]
	[InlineData("--version")]
	[InlineData("--json")]
	public void EndOfOptions_TreatsFlagLikeNamesAsTargets(string name)
	{
		using var fixture = new MultiPathFixture();
		fixture.Source(name + "/Source.cs", BadSource);
		var original = System.Environment.CurrentDirectory;
		try
		{
			System.Environment.CurrentDirectory = fixture.Root;
			var result = Invoke(["check", "--json", "--rule", "RAI001", "--", name]);
			Assert.Equal(1, result.Exit);
			using var json = JsonDocument.Parse(result.Output);
			Assert.Single(json.RootElement.EnumerateArray());
		}
		finally { System.Environment.CurrentDirectory = original; }
	}

	[Theory]
	[InlineData("check", "missing", false)]
	[InlineData("check", "missing", true)]
	[InlineData("fix", "missing", false)]
	[InlineData("fix", "unsupported.txt", false)]
	[InlineData("check", "unsupported.txt", true)]
	public void InvalidLaterTarget_FailsBeforeAnyEdits(string command, string invalidName, bool json)
	{
		using var fixture = new MultiPathFixture();
		var first = fixture.Source("A.cs", BadSource);
		var invalid = Path.Combine(fixture.Root, invalidName);
		if (invalidName.EndsWith(".txt")) File.WriteAllText(invalid, "unsupported");
		var result = Invoke([command, first, invalid, .. json ? new[] { "--json" } : new[] { "-n" }]);
		Assert.Equal(2, result.Exit);
		Assert.Contains(invalid, result.Error);
		Assert.DoesNotContain("No proven", result.Output);
		Assert.Equal(BadSource, File.ReadAllText(first));
		if (json) Assert.Equal("[]", result.Output.Trim());
	}

	[Theory]
	[InlineData("--rul", "RAI001")]
	[InlineData("--rule", null)]
	[InlineData("--group", null)]
	[InlineData("-p", null)]
	[InlineData("--path", "--rule")]
	public void InvalidOptions_FailWithoutNoisyJsonOrChanges(string option, string? value)
	{
		using var fixture = new MultiPathFixture();
		var source = fixture.Source("A.cs", BadSource);
		var result = Invoke(["check", source, "--json", option, .. value == null ? Array.Empty<string>() : new[] { value }]);
		Assert.Equal(2, result.Exit);
		Assert.Equal("[]", result.Output.Trim());
		Assert.Contains(option, result.Error);
		Assert.Equal(BadSource, File.ReadAllText(source));
	}

	[Fact]
	public void GroupListingCannotSilentlyDiscardSourceTargets()
	{
		using var fixture = new MultiPathFixture();
		var source = fixture.Source("Source.cs", BadSource);
		var result = Invoke(["check", source, "--groups", "--json"]);
		Assert.Equal(2, result.Exit);
		Assert.Equal("[]", result.Output.Trim());
		Assert.NotEmpty(result.Error);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void JsonScan_ContainsOnlyAnArrayWithExistingRecordShape(bool violation)
	{
		using var fixture = new MultiPathFixture();
		var a = fixture.Source("A.cs", CleanSource);
		var b = fixture.Source("B.cs", violation ? BadSource : CleanSource);
		var result = Invoke(["check", a, b, "--rule", "RAI001", "--json"]);
		Assert.Equal(violation ? 1 : 0, result.Exit);
		Assert.DoesNotContain('\u001b', result.Output);
		using var json = JsonDocument.Parse(result.Output);
		Assert.Equal(violation ? 1 : 0, json.RootElement.GetArrayLength());
		if (violation)
			Assert.Equal(new[] { "RuleId", "Message", "File", "Line", "Column", "Severity", "Suggestion", "Documentation" },
				json.RootElement[0].EnumerateObject().Select(p => p.Name));
	}

	[Fact]
	public void MultiPathFix_DryRunAndActualFix_ProcessEveryUniqueSource()
	{
		using var fixture = new MultiPathFixture();
		var a = fixture.Source("first/A.cs", BadSource);
		var b = fixture.Source("second/B.cs", BadSource);
		var fs = new TrackingFileSystem();
		var dry = Invoke(["fix", a, "--path", b, fixture.Root, "--rule", "RAI001", "--dry-run", "-n"], fs);
		Assert.Equal(0, dry.Exit);
		Assert.Contains(a, dry.Output);
		Assert.Contains(b, dry.Output);
		Assert.Empty(fs.Written);
		Assert.Equal(BadSource, File.ReadAllText(a));
		Assert.Equal(BadSource, File.ReadAllText(b));
		var applied = Invoke(["fix", a, b, fixture.Root, "--rule", "RAI001", "-n"], fs);
		Assert.Equal(0, applied.Exit);
		Assert.Equal(new[] { a, b }, fs.Written);
		Assert.All(new[] { a, b }, f => Assert.Contains("new RaiPath", File.ReadAllText(f)));
	}

	[Fact]
	public void DefaultSingleImplicitAndEmptyTargets_AndExcludedDirectories()
	{
		using var fixture = new MultiPathFixture();
		var source = fixture.Source("Source.cs", CleanSource);
		foreach (var name in new[] { "bin", "obj", ".git" }) fixture.Source(name + "/nested/Bad.cs", BadSource);
		var empty = fixture.Directory("empty");
		var original = System.Environment.CurrentDirectory;
		try
		{
			System.Environment.CurrentDirectory = fixture.Root;
			foreach (string[] args in new[] { new[] { "check", "-n" }, new[] { "check", source, "-n" }, new[] { fixture.Root, source, "-n" } })
			{
				var result = Invoke(args);
				Assert.Equal(0, result.Exit);
				Assert.Contains("Checked 1 files", result.Output);
			}
			var excluded = Invoke(["check", Path.Combine(fixture.Root, "obj"), "-n"]);
			Assert.Equal(0, excluded.Exit);
			Assert.Contains("No C# source files", excluded.Output);
			var zero = Invoke(["check", empty, "-n"]);
			Assert.Equal(0, zero.Exit);
			Assert.Contains("No C# source files", zero.Output);
			Assert.DoesNotContain("No proven", zero.Output);
		}
		finally { System.Environment.CurrentDirectory = original; }
	}

	[Theory]
	[InlineData(false, "fix")]
	[InlineData(true, "fix")]
	[InlineData(false, "check")]
	[InlineData(true, "check")]
	public void AccessFailuresBeforeFix_DoNotModifyAnySource(bool enumerationFailure, string command)
	{
		using var fixture = new MultiPathFixture();
		var first = fixture.Source("first/A.cs", BadSource);
		var second = fixture.Source("second/B.cs", BadSource);
		var failure = enumerationFailure ? Path.GetDirectoryName(second)! : second;
		var fs = new FailingFileSystem(failure, enumerationFailure ? "enumerate" : "validate");
		var result = Invoke([command, first, Path.GetDirectoryName(second)!, "--json"], fs);
		if (command == "check") Assert.Equal("[]", result.Output.Trim());
		Assert.Equal(2, result.Exit);
		Assert.Contains(failure, result.Error);
		Assert.Empty(fs.Written);
		Assert.All(new[] { first, second }, f => Assert.Equal(BadSource, File.ReadAllText(f)));
	}

	[Fact]
	public void LaterInspectionReadFailure_ProducesErrorAndEmptyJson_NotCleanSuccess()
	{
		using var fixture = new MultiPathFixture();
		var a = fixture.Source("A.cs", CleanSource);
		var b = fixture.Source("B.cs", BadSource);
		var result = Invoke(["check", a, b, "--json"], new FailingFileSystem(b, "read"));
		Assert.Equal(2, result.Exit);
		Assert.Equal("[]", result.Output.Trim());
		Assert.Contains(b, result.Error);
	}

	[Fact]
	public void LaterFixWriteFailure_ReportsPreviouslyModifiedFilesAndFailingPath()
	{
		using var fixture = new MultiPathFixture();
		var a = fixture.Source("A.cs", BadSource);
		var b = fixture.Source("B.cs", BadSource);
		var result = Invoke(["fix", a, b, "--rule", "RAI001", "-n"], new FailingFileSystem(b, "write"));
		Assert.Equal(2, result.Exit);
		Assert.Contains(a, result.Error);
		Assert.Contains(b, result.Error);
		Assert.DoesNotContain("Applied", result.Output);
	}

	[Fact]
	public void CaseAliasesCollapseOnlyOnCaseInsensitiveFilesystems()
	{
		using var fixture = new MultiPathFixture();
		var upper = fixture.Source("UPPER.cs", CleanSource);
		var lower = Path.Combine(fixture.Root, "upper.cs");
		var caseInsensitive = File.Exists(lower);
		if (!caseInsensitive) File.WriteAllText(lower, CleanSource);
		var files = SourceSelection.Resolve([upper, lower, fixture.Root], new SourceFileSystem());
		Assert.Equal(caseInsensitive ? 1 : 2, files.Count);
	}

	[Fact]
	public void SharedCompilation_ResolvesDeclarationsAndExemptionsInAnotherTarget()
	{
		using var fixture = new MultiPathFixture();
		var declaration = fixture.Source("model/Shared.cs", "using JsonPit; public class Shared { public Pit Pit = null!; }\n[OsLib.RaiRuleExemption(\"RAI015\", \"Cross-file subject declaration.\")] partial class Subject { }\n");
		var source = fixture.Source("consumer/Use.cs", """
			using System;
			using JsonPit;
			using Newtonsoft.Json.Linq;
			using OsLib;
			partial class Subject { void ParserSubject() { JObject.Parse("{}"); } }
			class Use
			{
				void Bad(Shared state)
				{
					var projection = state.Pit.GetAt("A1", DateTimeOffset.UtcNow);
					projection.ExtendWith(new JObject { ["Name"] = "changed" });
					state.Pit.Add(projection);
				}
				[RaiRuleExemption("RAI015", "Parser behavior is this test's subject.")]
				void ParserSubject() { JObject.Parse("{}"); }
			}
			""");
		var result = Invoke(["check", declaration, source, "--rule", "RAI011,RAI015", "--json"]);
		Assert.Equal(1, result.Exit);
		using var json = JsonDocument.Parse(result.Output);
		Assert.Contains(json.RootElement.EnumerateArray(), r => r.GetProperty("RuleId").GetString() == "RAI011");
		Assert.DoesNotContain(json.RootElement.EnumerateArray(), r => r.GetProperty("RuleId").GetString() == "RAI015");
	}

	private static (int Exit, string Output, string Error) Invoke(string[] args, SourceFileSystem? fileSystem = null)
	{
		var oldOut = Console.Out;
		var oldError = Console.Error;
		using var output = new StringWriter();
		using var error = new StringWriter();
		try
		{
			Console.SetOut(output);
			Console.SetError(error);
			var exit = fileSystem == null ? Program.Main(args) : Program.Run(args, fileSystem);
			return (exit, output.ToString(), error.ToString());
		}
		finally { Console.SetOut(oldOut); Console.SetError(oldError); }
	}

	private sealed class MultiPathFixture : IDisposable
	{
		internal string Root { get; } = Path.Combine(Path.GetTempPath(), "rail-cr078-" + Guid.NewGuid().ToString("N"));
		internal MultiPathFixture() => System.IO.Directory.CreateDirectory(Root);
		internal string Directory(string relative)
		{
			var path = Path.Combine(Root, relative);
			System.IO.Directory.CreateDirectory(path);
			return path;
		}
		internal string Source(string relative, string content)
		{
			var path = Path.Combine(Root, relative);
			System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
			return path;
		}
		public void Dispose() => System.IO.Directory.Delete(Root, true);
	}

	private class TrackingFileSystem : SourceFileSystem
	{
		internal List<string> Written { get; } = [];
		internal override void WriteAllText(string path, string content)
		{
			base.WriteAllText(path, content);
			Written.Add(path);
		}
	}

	private sealed class FailingFileSystem(string path, string operation) : TrackingFileSystem
	{
		private void Fail(string actual, string op)
		{
			if (actual == path && operation == op) throw new UnauthorizedAccessException($"Injected {op} denial for '{actual}'.");
		}
		internal override IEnumerable<string> Entries(string directory) { Fail(directory, "enumerate"); return base.Entries(directory); }
		internal override void ValidateReadable(string file) { Fail(file, "validate"); base.ValidateReadable(file); }
		internal override string ReadAllText(string file) { Fail(file, "read"); return base.ReadAllText(file); }
		internal override void WriteAllText(string file, string content) { Fail(file, "write"); base.WriteAllText(file, content); }
	}
}
