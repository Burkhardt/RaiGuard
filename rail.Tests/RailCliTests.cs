using System;
using System.IO;
using RaiGuard.Cli;
using Xunit;

namespace RaiGuard.Tests;

public class RailCliTests
{
    [Fact]
    public void Version_ReturnsVersionString()
    {
        var version = Program.GetVersion();
        Assert.StartsWith("rail v", version);
    }

    [Fact]
    public void Main_VersionFlag_ReturnsZero()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["-v"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("rail v", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Main_HelpFlag_ReturnsZero()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["-h", "-n"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("Commands:", sw.ToString());
            Assert.Contains("check", sw.ToString());
            Assert.Contains("fix", sw.ToString());
            Assert.Contains("rules", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Main_RulesCommand_ListsRAI001()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["rules", "-n"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("RAI001", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Check_DetectsSystemIoDirectory()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var files = Directory.GetFiles(""."");
    }
}");

        try
        {
            var exitCode = Program.Main(["check", "-p", tempFile, "-n", "--json"]);
            Assert.Equal(1, exitCode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Fix_RewritesDirectoryToRaiPath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var files = Directory.GetFiles(""."");
    }
}");

        try
        {
            var exitCode = Program.Main(["fix", "-p", tempFile, "-n"]);
            Assert.Equal(0, exitCode);

            var updated = File.ReadAllText(tempFile);
            Assert.Contains("RaiPath.EnumerateFiles", updated);
            Assert.DoesNotContain("Directory.GetFiles", updated);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Main_RulesCommand_ListsRAI002()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["rules", "-n"]);
            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("RAI002", output);
            Assert.Contains("Prohibit direct usage of System.IO.File", output);
            Assert.Contains("rm()", output);
            Assert.Contains("TextFile", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Main_RulesCommand_Json_IncludesRAI002()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["rules", "--json"]);
            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("\"Id\": \"RAI001\"", output);
            Assert.Contains("\"Id\": \"RAI002\"", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Check_DetectsSystemIoFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var exists = File.Exists(""test.txt"");
    }
}");

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["check", "-p", tempFile, "-n", "--json"]);
            Assert.Equal(1, exitCode);
            Assert.Contains("RAI002", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Fix_RewritesFileToRaiFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var exists = File.Exists(""test.txt"");
        var lines = File.ReadAllLines(""test.txt"");
        var text = File.ReadAllText(""test.txt"");
        File.WriteAllText(""test.txt"", ""hello"");
        File.Delete(""test.txt"");
    }
}");

        try
        {
            var exitCode = Program.Main(["fix", "-p", tempFile, "-n"]);
            Assert.Equal(0, exitCode);

            var updated = File.ReadAllText(tempFile);
            Assert.Contains("using OsLib;", updated);
            Assert.Contains("new RaiFile(\"test.txt\").Exists()", updated);
            Assert.Contains("new TextFile(\"test.txt\").Read()", updated);
            Assert.Contains("new TextFile(\"test.txt\").ReadAllText()", updated);
            Assert.Contains("new TextFile(\"test.txt\", \"hello\")", updated);
            Assert.Contains("new RaiFile(\"test.txt\").rm()", updated);
            Assert.DoesNotContain("File.Exists", updated);
            Assert.DoesNotContain("File.ReadAllLines", updated);
            Assert.DoesNotContain("File.ReadAllText", updated);
            Assert.DoesNotContain("File.WriteAllText", updated);
            Assert.DoesNotContain("File.Delete", updated);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Main_RulesCommand_ListsRAI003AndRAI010()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["rules", "-n"]);
            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("RAI003", output);
            Assert.Contains("Prohibit direct usage of System.IO.Path", output);
            Assert.Contains("RAI010", output);
            Assert.Contains("Enforce tab indentation and prohibit consecutive spaces", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Main_RulesCommand_Json_IncludesRAI003AndRAI010()
    {
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["rules", "--json"]);
            Assert.Equal(0, exitCode);
            var output = sw.ToString();
            Assert.Contains("\"Id\": \"RAI003\"", output);
            Assert.Contains("\"Id\": \"RAI010\"", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Check_DetectsSystemIoPath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var p = Path.Combine(""dir"", ""file.txt"");
    }
}");

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["check", "-p", tempFile, "-n", "--json"]);
            Assert.Equal(1, exitCode);
            Assert.Contains("RAI003", sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Check_DetectsWhitespaceFormatting_SpaceIndent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile,
"using System;\n" +
"\n" +
"public class Sample\n" +
"{\n" +
"    public void Run()\n" +
"    {\n" +
"    }\n" +
"}");

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var exitCode = Program.Main(["check", "-p", tempFile, "-n", "--json"]);
            Assert.Equal(1, exitCode);
            var output = sw.ToString();
            Assert.Contains("RAI010", output);
            Assert.Contains("Indentation must use tabs", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Fix_RewritesPathCombineToSlashOperator()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"RailTest_{Guid.NewGuid():N}.cs");
        File.WriteAllText(tempFile, @"
using System;
using System.IO;

public class Sample
{
    public void Run()
    {
        var p = Path.Combine(""dir"", ""sub"", ""file.txt"");
    }
}");

        try
        {
            var exitCode = Program.Main(["fix", "-p", tempFile, "-n"]);
            Assert.Equal(0, exitCode);

            var updated = File.ReadAllText(tempFile);
            Assert.Contains("using OsLib;", updated);
            Assert.Contains("new RaiPath(\"dir\") / \"sub\" / \"file.txt\"", updated);
            Assert.DoesNotContain("Path.Combine", updated);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
