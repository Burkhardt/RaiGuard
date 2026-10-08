using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using RaiGuard.Core.Rules;
using Xunit;

namespace RaiGuard.Tests;

public class WhitespaceFormattingAnalyzerTests
{
	[Fact]
	public async Task WhenCleanTabbedCode_NoDiagnostic()
	{
		var testCode =
"namespace TestNamespace\n" +
"{\n" +
"\tpublic class TestClass\n" +
"\t{\n" +
"\t\tpublic void TestMethod()\n" +
"\t\t{\n" +
"\t\t\tvar str = \"hello\";\n" +
"\t\t}\n" +
"\t}\n" +
"}";

		await CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
	}

	[Fact]
	public async Task WhenStringsAndCharsWithSpaces_NoDiagnostic()
	{
		var testCode =
"namespace TestNamespace\n" +
"{\n" +
"\tpublic class TestClass\n" +
"\t{\n" +
"\t\tpublic void TestMethod()\n" +
"\t\t{\n" +
"\t\t\tvar regular = \"hello   world\";\n" +
"\t\t\tvar verbatim = @\"multi   line\n" +
"    spaced line\";\n" +
"\t\t\tvar interpolated = $\"value   {regular}   end\";\n" +
"\t\t\tvar ch = ' ';\n" +
"\t\t}\n" +
"\t}\n" +
"}";

		await CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
	}

	[Fact]
	public async Task WhenSpaceIndentation_EmitsDiagnostic()
	{
		var testCode =
"namespace TestNamespace\n" +
"{\n" +
"{|#0:    |}public class TestClass\n" +
"\t{\n" +
"\t}\n" +
"}";

		var expected = CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>
			.Diagnostic("RAI010")
			.WithLocation(0)
			.WithArguments(WhitespaceFormattingAnalyzer.TabIndentationMessage);

		await CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}

	[Fact]
	public async Task WhenConsecutiveSpacesBetweenTokens_EmitsDiagnostic()
	{
		var testCode =
"namespace TestNamespace\n" +
"{\n" +
"\tpublic class TestClass\n" +
"\t{\n" +
"\t\tpublic void TestMethod()\n" +
"\t\t{\n" +
"\t\t\tvar{|#0:  |}x = 1;\n" +
"\t\t}\n" +
"\t}\n" +
"}";

		var expected = CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>
			.Diagnostic("RAI010")
			.WithLocation(0)
			.WithArguments(WhitespaceFormattingAnalyzer.ConsecutiveSpacesMessage);

		await CSharpAnalyzerVerifier<WhitespaceFormattingAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
	}
}
