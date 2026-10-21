using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using RaiGuard.Core.Rules;
using Xunit;

namespace RaiGuard.Tests;

public class RaiKeepArchitectureAnalyzerTests
{
	[Fact]
	public async Task RAI001_WhenMethodGroupReference_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;
using System.Linq;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod(string[] dirs)
		{
			var files = dirs.Select(Directory.{|#0:GetFiles|}).ToList();
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>
			.Diagnostic("RAI001")
			.WithLocation(0)
			.WithArguments("GetFiles");

		await CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI001_WhenNewDirectoryInfo_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var di = new {|#0:DirectoryInfo|}(""."");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>
			.Diagnostic("RAI001")
			.WithLocation(0)
			.WithArguments("DirectoryInfo");

		await CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI002_WhenNewFileInfo_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var fi = new {|#0:FileInfo|}(""test.txt"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
			.Diagnostic("RAI002")
			.WithLocation(0)
			.WithArguments("FileInfo");

		await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI003_WhenPathGetFileNameMethodGroup_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;
using System.Linq;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod(string[] paths)
		{
			var names = paths.Select(Path.{|#0:GetFileName|}).ToList();
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>
			.Diagnostic("RAI003")
			.WithLocation(0)
			.WithArguments("GetFileName");

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI003_WhenPathDirectorySeparatorChar_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;

namespace TestNamespace
{
	public class TestClass
	{
		public char TestMethod()
		{
			return Path.{|#0:DirectorySeparatorChar|};
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>
			.Diagnostic("RAI003")
			.WithLocation(0)
			.WithArguments("DirectorySeparatorChar");

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI004_WhenRaiPathWithoutTrailingSlash_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class RaiPath
	{
		public RaiPath(string path) { }
	}

	public class TestClass
	{
		public void TestMethod()
		{
			var root = new RaiPath({|#0:""/Users/rsb/temp/AfricaStage_TestRoot""|});
			var valid = new RaiPath(""/Users/rsb/temp/AfricaStage_TestRoot/"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI004")
			.WithLocation(0)
			.WithArguments("/Users/rsb/temp/AfricaStage_TestRoot");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI005_WhenPathStringConcatenationWithOsDir_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public static class Os { public const string DIR = ""/""; }
	public class RaiPath { public RaiPath(string p) { } }
	public class TextFile { public TextFile(string p) { } }

	public class TestClass
	{
		public void TestMethod(RaiPath promptDir)
		{
			var f = new TextFile({|#0:promptDir.ToString() + Os.DIR + ""OpmConditionAuthoring.system.md""|});
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI005")
			.WithLocation(0)
			.WithArguments("promptDir.ToString() + Os.DIR + \"OpmConditionAuthoring.system.md\"", "TextFile");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI006_WhenUntypedRaiSystemPits_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class RaiSystem
	{
		public RaiSystem(string exe, string args) { }
	}

	public class TestClass
	{
		public void TestMethod()
		{
			var sys = {|#0:new RaiSystem(""pits"", ""list"")|};
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI006")
			.WithLocation(0)
			.WithArguments("new RaiSystem(\"pits\", ...)", "PitsCommand");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI007_WhenEnvironmentSetEnvironmentVariable_EmitsDiagnostic()
	{
		var testCode = @"
using System;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			Environment.{|#0:SetEnvironmentVariable|}(""Cloud__Root"", ""/tmp"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI007")
			.WithLocation(0)
			.WithArguments("SetEnvironmentVariable");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI008_WhenRmCalledBeforeSave_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class TextFile
	{
		public void rm() { }
		public void Save() { }
	}

	public class TestClass
	{
		public void TestMethod(TextFile tf)
		{
			tf.rm();
			tf.{|#0:Save|}();
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI008")
			.WithLocation(0)
			.WithArguments("Calling 'tf.rm()' before 'tf.Save()' destroys cloud in-place file identity (CR022); truncate and overwrite in-place via 'tf.DeleteAll().Append(...).Save()' or 'tf.cp(source)'");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI009_WhenInvertedCpReceiver_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class RaiFile
	{
		public void cp(RaiFile from) { }
	}

	public class TestClass
	{
		public void TestMethod(RaiFile sourceFile, RaiFile destFile)
		{
			sourceFile.{|#0:cp|}(destFile);
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI009")
			.WithLocation(0)
			.WithArguments("sourceFile", "cp", "destFile");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI011_WhenPitReadModifyWrite_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class PitItem
	{
		public PitItem(string id) { }
		public void ExtendWith(object patch) { }
	}
	public class Pit
	{
		public PitItem Get(string id) => new PitItem(id);
		public void Add(PitItem item) { }
	}

	public class TestClass
	{
		public void TestMethod(Pit activityPit, object patch)
		{
			var existing = activityPit.Get(""Sipho"");
			existing.ExtendWith(patch);
			{|#0:activityPit.Add(existing)|};

			// Valid sparse delta idiom — no diagnostic
			var delta = new PitItem(""Sipho"");
			delta.ExtendWith(patch);
			activityPit.Add(delta);
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI011")
			.WithLocation(0)
			.WithArguments("existing");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI011_WhenFreshlyGeneratedCollectionOrTargetTypedNew_DoesNotEmitDiagnostic()
	{
		var testCode = @"
using System.Collections.Generic;

namespace TestNamespace
{
	public class PitItem
	{
		public PitItem(string id) { }
		public void ExtendWith(object patch) { }
	}
	public class Pit
	{
		public void Add(PitItem item) { }
	}

	public class UseCaseInstantiator
	{
		public void Spawn(Pit activityPit, object patch)
		{
			var generated = new List<PitItem> { new PitItem(""act-1"") };
			foreach (var item in generated)
			{
				item.ExtendWith(patch);
			}
			foreach (var item in generated)
			{
				activityPit.Add(item);
			}

			PitItem marker = new(""baseline"");
			marker.ExtendWith(patch);
			activityPit.Add(marker);
		}
	}
}";

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
	}

	[Fact]
	public async Task RAI011_WhenParameterOrCoalescedLookupReadModifyWrite_EmitsDiagnostic()
	{
		var testCode = @"
using System;

namespace TestNamespace
{
	public class PitItem
	{
		public PitItem(string id) { }
		public void ExtendWith(object patch) { }
	}
	public class Pit
	{
		public PitItem? this[string id] => new PitItem(id);
		public void Add(PitItem item) { }
	}

	public class Instantiator
	{
		public void Graft(Pit activityPit, PitItem existingActivity, object patch)
		{
			existingActivity.ExtendWith(patch);
			{|#0:activityPit.Add(existingActivity)|};
		}

		public void BindFiller(Pit objectPit, string objectId, object patch)
		{
			var obj = objectPit[objectId] ?? throw new InvalidOperationException();
			{|#1:objectPit.Add(obj)|};
		}
	}
}";

		var expected0 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI011")
			.WithLocation(0)
			.WithArguments("existingActivity");

		var expected1 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI011")
			.WithLocation(1)
			.WithArguments("obj");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected0, expected1);
	}

	[Fact]
	public async Task RAI012_WhenProtectedAttributeInExtendWith_EmitsDiagnostic()
	{
		var testCode = @"
using System.Collections.Generic;

namespace TestNamespace
{
	public class PitItem
	{
		public void ExtendWith(Dictionary<string, object> patch) { }
	}

	public class TestClass
	{
		public void TestMethod(PitItem delta)
		{
			delta.ExtendWith(new Dictionary<string, object>
			{
				{|#0:[""Id""] = ""Sipho""|},
				[""Role""] = ""Sax""
			});
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI012")
			.WithLocation(0)
			.WithArguments("Id");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task RAI013_WhenPitDirExistsOrPitReload_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class RaiPath { public bool Exists() => true; }
	public class Pit
	{
		public RaiPath PitDir => new RaiPath();
		public void Reload() { }
	}

	public class TestClass
	{
		public void TestMethod(Pit userPit)
		{
			if (userPit.{|#0:PitDir|}.Exists())
			{
				userPit.{|#1:Reload|}();
			}
		}
	}
}";

		var expected0 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI013")
			.WithLocation(0)
			.WithArguments("'userPit.PitDir.Exists' triggers a disk 'mkdir()' side effect via the 'PitDir' getter; use 'userPit.JsonFile.Path.Exists()' or 'userPit.JsonFile.Exists()' instead");

		var expected1 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI013")
			.WithLocation(1)
			.WithArguments("'userPit.Reload()' invokes 'Save()' on dirty/merge branches and can overwrite external CLI seeds on disk; use 'userPit.Load()' for pure disk-to-memory reload");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected0, expected1);
	}

	[Fact]
	public async Task RAI014_WhenPitNotDisposed_EmitsDiagnostic()
	{
		var testCode = @"
using System;

namespace TestNamespace
{
	public class Pit : IDisposable
	{
		public Pit(string path) { }
		public void Dispose() { }
	}

	public class LeakyTest : IDisposable
	{
		private readonly Pit {|#0:_personPit|};

		public LeakyTest()
		{
			_personPit = new Pit(""Person"");
		}

		public void RunLocal()
		{
			var {|#1:localPit|} = new Pit(""Local"");
			using var cleanPit = new Pit(""Clean"");
		}

		public void Dispose()
		{
		}
	}
}";

		var expected0 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI014")
			.WithLocation(0)
			.WithArguments("_personPit");

		var expected1 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI014")
			.WithLocation(1)
			.WithArguments("localPit");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected0, expected1);
	}

	[Fact]
	public async Task RAI015_WhenTemplatePitItemIdOrJObjectParse_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class PitItem { public PitItem(string id) { } }
	public class JObject { public static JObject Parse(string json) => new JObject(); }

	public class TestClass
	{
		public void TestMethod(string raw)
		{
			var item = new PitItem({|#0:""{UnexpandedId}""|});
			var obj = {|#1:JObject.Parse|}(raw);
		}
	}
}";

		var expected0 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI015")
			.WithLocation(0)
			.WithArguments("Invalid live PitItem ID '{UnexpandedId}' violates CR049 (PitItem.ValidateLiveId rejects empty/whitespace IDs and unexpanded '{' or '<' template placeholders)");

		var expected1 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI015")
			.WithLocation(1)
			.WithArguments("'JObject.Parse(...)' uses default 'DateParseHandling.DateTime' which corrupts ISO-8601 strings in local timezones; parse with 'JsonTextReader { DateParseHandling = DateParseHandling.None }' or 'PitJson' / 'RaidJson5'");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected0, expected1);
	}

	[Fact]
	public async Task RAI016_WhenDiagramBuilderBaseIdHasUnderscoreOrLegacyNaming_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public enum ImageNamingConvention { Legacy, Structured }
	public class OneUseCaseDiagramBuilder
	{
		public OneUseCaseDiagramBuilder(string baseItemId) { }
	}

	public class TestClass
	{
		public void TestMethod()
		{
			var b = new OneUseCaseDiagramBuilder({|#0:""Checkout_UCD""|});
			var conv = {|#1:ImageNamingConvention.Legacy|};
		}
	}
}";

		var expected0 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI016")
			.WithLocation(0)
			.WithArguments("DiagramBuilder baseItemId 'Checkout_UCD' must be alphanumeric without '_' or pre-appended archetype suffixes (CR025); the builder appends the archetype suffix automatically");

		var expected1 = CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>
			.Diagnostic("RAI016")
			.WithLocation(1)
			.WithArguments("'ImageNamingConvention.Legacy' is deprecated for new multi-image and diagram assets; prefer 'ImageNamingConvention.Structured'");

		await CSharpAnalyzerVerifier<RaiKeepArchitectureAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected0, expected1);
	}
}
