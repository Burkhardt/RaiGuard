using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using RaiGuard.Core.Rules;
using Xunit;

namespace RaiGuard.Tests;

public class SystemIoPathAnalyzerTests
{
	[Fact]
	public async Task WhenValidCode_NoDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var str = ""hello"";
		}
	}
}";

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
	}

	[Fact]
	public async Task WhenPathCombine_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var p = Path.{|#0:Combine|}(""a"", ""b"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>
			.Diagnostic("RAI003")
			.WithLocation(0)
			.WithArguments("Combine");

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task WhenFullyQualifiedSystemIoPath_EmitsDiagnostic()
	{
		var testCode = @"
namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var p = System.IO.Path.{|#0:Combine|}(""a"", ""b"", ""c"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>
			.Diagnostic("RAI003")
			.WithLocation(0)
			.WithArguments("Combine");

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task WhenPathGetFileName_EmitsDiagnostic()
	{
		var testCode = @"
using System.IO;

namespace TestNamespace
{
	public class TestClass
	{
		public void TestMethod()
		{
			var name = Path.{|#0:GetFileName|}(""path/file.txt"");
		}
	}
}";

		var expected = CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>
			.Diagnostic("RAI003")
			.WithLocation(0)
			.WithArguments("GetFileName");

		await CSharpAnalyzerVerifier<SystemIoPathAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}
}
