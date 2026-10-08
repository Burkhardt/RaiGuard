using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using RaiGuard.Core.Rules;
using Xunit;

namespace RaiGuard.Tests;

public class SystemIoFileAnalyzerTests
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

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode);
    }

    [Fact]
    public async Task WhenFileExists_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var exists = File.{|#0:Exists|}(""file.txt"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("Exists");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task WhenFileReadAllText_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var text = File.{|#0:ReadAllText|}(""file.txt"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("ReadAllText");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task WhenFileReadAllLines_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var lines = File.{|#0:ReadAllLines|}(""file.txt"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("ReadAllLines");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task WhenFileWriteAllText_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            File.{|#0:WriteAllText|}(""file.txt"", ""content"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("WriteAllText");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task WhenFileDelete_EmitsDiagnostic()
    {
        var testCode = @"
using System.IO;

namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            File.{|#0:Delete|}(""file.txt"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("Delete");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }

    [Fact]
    public async Task WhenFullyQualifiedSystemIoFile_EmitsDiagnostic()
    {
        var testCode = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod()
        {
            var exists = System.IO.File.{|#0:Exists|}(""file.txt"");
        }
    }
}";

        var expected = CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>
            .Diagnostic("RAI002")
            .WithLocation(0)
            .WithArguments("Exists");

        await CSharpAnalyzerVerifier<SystemIoFileAnalyzer, DefaultVerifier>.VerifyAnalyzerAsync(testCode, expected);
    }
}
