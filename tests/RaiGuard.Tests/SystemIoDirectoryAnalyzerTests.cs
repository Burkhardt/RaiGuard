using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using RaiGuard.Core.Rules;
using Xunit;

namespace RaiGuard.Tests;

public class SystemIoDirectoryAnalyzerTests
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

        await CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task WhenDirectoryGetFiles_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var files = Directory.{|#0:GetFiles|}(""."");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>
            .Diagnostic("RAI001")
            .WithLocation(0)
            .WithArguments("GetFiles");

        await CSharpAnalyzerVerifier<SystemIoDirectoryAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }
}
