using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using OsLib;
using RaiGuard.Core.Diagnostics;
using RaiGuard.Core.Helpers;
using RaiGuard.Core.Rules;

namespace RaiGuard;

public static class Icons
{
	public const char Error = '\uea87';
	public const char Warning = '\uf071';
	public const char Success = '\ueab2';
	public const char Info = '\uea74';
	public const char Help = '\uf059';
	public const char NotAvailable = '\ueabd';
	public const char File = '\uea7b';
	public const char Folder = '\uea83';
	public const char Banner = '\ueb1e';
	public const char NoBanner = '\ueb24';
	public const char Bug = '\uf188';
	public const char Runner = '\uf04b';
	public const char Force = '\uf0e7';
	public const char Shield = '\uf3ed';
	public const char Wrench = '\uf0ad';
	public const char Check = '\uf00c';
	public const string DropboxBoxOutline = "\U000F0BF4";
	public const string GoogleDriveBoxOutline = "\U000F0BFD";
	public const string ICloudDriveBoxOutline = "\U000F0C03";
	public const string OneDriveBoxOutline = "\U000F0C15";
	public const string HelpLineWidthCompensation = "  ";
	public static readonly string[] NumberBoxOutlines =
	[
		"\U000F03A6", "\U000F03A9", "\U000F03AC", "\U000F03AE", "\U000F03B0",
		"\U000F03B5", "\U000F03B8", "\U000F03BB", "\U000F03BE"
	];
}

public static class Messages
{
	public static bool Debug { get; set; } = false;
	public static bool Banner { get; set; } = true;
	public static bool Json { get; set; } = false;
	public static string? TargetPath { get; set; }
	public static string? TargetRule { get; set; }

	public static string[] Help =>
	[
		$"Commands:\t{Icons.Info}\tcheck, fix, rules",
		$"  rail check [<path>] [--rule <id>] [--json]",
		$"  rail fix [<path>] [--rule <id>] [--dry-run]",
		$"  rail rules [--json]",
		$"-h, --help\t{Icons.Help}\tprint out all options",
		$"-v, --version\t{Icons.Info}\tprint version info",
		$"-n, --nologo\t{(Banner ? Icons.Banner : Icons.NoBanner)}\tdo not display the banner",
		$"-b, --debug\t{(Debug ? Icons.Bug : Icons.Runner)}\tenable debug output",
		$"-p, --path\t{Icons.Folder}\t{PathDescription()}",
		$"--rule\t\t{Icons.Shield}\t{RuleDescription()}",
		$"--json\t\t{(Json ? Icons.Success : Icons.NotAvailable)}\toutput diagnostics in JSON format",
		$"--dry-run\t{Icons.Info}\tsimulate code fixes without modifying source files on disk",
		$"{Icons.Info} Rules\t{Icons.Shield}\tRAI001 (Directory), RAI002 (File), RAI003 (Path), RAI010 (Formatting)",
	];

	private static string PathDescription() =>
		!string.IsNullOrWhiteSpace(TargetPath) ? TargetPath : "target file or directory (defaults to current directory)";

	private static string RuleDescription() =>
		!string.IsNullOrWhiteSpace(TargetRule) ? TargetRule : "filter by rule ID (e.g. RAI001)";

	public static void WriteHighlighted(string text, ConsoleColor foreground = ConsoleColor.Black, ConsoleColor? background = null)
	{
		var oldForeground = Console.ForegroundColor;
		var oldBackground = Console.BackgroundColor;
		Console.ForegroundColor = foreground;
		Console.BackgroundColor = background ?? oldBackground;
		Console.WriteLine(text);
		Console.ForegroundColor = oldForeground;
		Console.BackgroundColor = oldBackground;
	}

	public static void WriteError(string text) => WriteHighlighted(text, ConsoleColor.DarkRed, ConsoleColor.White);
	public static void WriteWarning(string text) => WriteHighlighted(text, ConsoleColor.DarkYellow);
	public static void WriteSuccess(string text) => WriteHighlighted(text, ConsoleColor.DarkGreen);
	public static void WriteInfo(string text) => WriteHighlighted(text, ConsoleColor.Blue);
	public static void WriteDebug(string text) { if (Debug) WriteHighlighted(text, ConsoleColor.DarkMagenta); }

	public static void WriteLine(string text, char underlineChar = '─')
	{
		for (int i = 0; i < text.Length; i++) Console.Write(underlineChar);
		Console.WriteLine();
	}

	public static void WriteBanner(string text)
	{
		Console.Write($"{Icons.Banner} ");
		WriteLine(text);
		WriteBannerContent(text);
		Console.Write($"{Icons.Banner} ");
		WriteLine(text);
	}

	private static void WriteBannerContent(string text)
	{
		var oldColor = Console.ForegroundColor;
		int index = 0;
		while (index < text.Length)
		{
			int nextRail = text.IndexOf("rail", index, StringComparison.Ordinal);
			if (nextRail < 0)
			{
				Console.Write(text[index..]);
				break;
			}

			if (nextRail > index)
			{
				Console.Write(text[index..nextRail]);
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("rail");
			Console.ForegroundColor = oldColor;
			index = nextRail + 4;
		}
		Console.WriteLine();
	}

	public static void WriteHelp()
	{
		foreach (var line in Help) WriteSuccess(line + Icons.HelpLineWidthCompensation);
	}
}

public static class Program
{
	private const string CliSubscriber = "rail";
	private const string BannerTitle = "rail - RaiGuard Roslyn Guardrails & Analyzer for the RAIkeep Framework";
	private static readonly string[] Commands = ["check", "fix", "rules"];
	private static readonly string[] SwitchesWithValues = ["-p", "--path", "--rule"];

	public static int Main(string[] args)
	{
		if (HasOption(args, "-v", "--version"))
		{
			Messages.WriteSuccess(GetVersion());
			return 0;
		}

		if (HasOption(args, "-h", "--help"))
		{
			if (!HasOption(args, "-n", "-l", "--nologo"))
			{
				Messages.WriteBanner(BannerTitle);
			}
			Messages.WriteHelp();
			return 0;
		}

		if (args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
		{
			return RunCommand(args[0].ToLowerInvariant(), args[1..]);
		}

		return RunMappedArguments(args);
	}

	public static int RunCommand(string command, string[] args)
	{
		Messages.Debug = HasOption(args, "-b", "-d", "--debug");
		Messages.Banner = !HasOption(args, "-n", "-l", "--nologo");
		Messages.Json = HasOption(args, "--json");
		bool dryRun = HasOption(args, "--dry-run");

		var pathParam = ParamValue(args, "-p", "--path") ?? PositionalArg(args);
		var ruleParam = ParamValue(args, "--rule");

		Messages.TargetPath = pathParam;
		Messages.TargetRule = ruleParam;

		if (Messages.Banner && !Messages.Json)
		{
			Messages.WriteBanner(BannerTitle);
		}

		return command switch
		{
			"check" => RunCheck(pathParam, ruleParam, Messages.Json),
			"fix" => RunFix(pathParam, ruleParam, dryRun, Messages.Json),
			"rules" => RunRules(Messages.Json),
			_ => RunUnknownCommand(command)
		};
	}

	private static int RunMappedArguments(string[] args)
	{
		Messages.Debug = HasOption(args, "-b", "-d", "--debug");
		Messages.Banner = !HasOption(args, "-n", "-l", "--nologo");
		Messages.Json = HasOption(args, "--json");

		var pathParam = ParamValue(args, "-p", "--path") ?? PositionalArg(args);
		var ruleParam = ParamValue(args, "--rule");

		if (Messages.Banner && !Messages.Json)
		{
			Messages.WriteBanner(BannerTitle);
		}

		// Default action is check
		return RunCheck(pathParam, ruleParam, Messages.Json);
	}

	private static int RunRules(bool json)
	{
		var rules = new[]
		{
			new
			{
				Id = DiagnosticDescriptors.ProhibitSystemIoDirectory.Id,
				Title = DiagnosticDescriptors.ProhibitSystemIoDirectory.Title.ToString(),
				Category = DiagnosticDescriptors.ProhibitSystemIoDirectory.Category,
				Severity = DiagnosticDescriptors.ProhibitSystemIoDirectory.DefaultSeverity.ToString(),
				Description = DiagnosticDescriptors.ProhibitSystemIoDirectory.Description.ToString(),
				Remediation = "Use RaiPath methods (e.g. RaiPath.EnumerateDirectories, RaiPath.EnumerateFiles)"
			},
			new
			{
				Id = DiagnosticDescriptors.ProhibitSystemIoFile.Id,
				Title = DiagnosticDescriptors.ProhibitSystemIoFile.Title.ToString(),
				Category = DiagnosticDescriptors.ProhibitSystemIoFile.Category,
				Severity = DiagnosticDescriptors.ProhibitSystemIoFile.DefaultSeverity.ToString(),
				Description = DiagnosticDescriptors.ProhibitSystemIoFile.Description.ToString(),
				Remediation = "Use RaiFile or TextFile methods (e.g. new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))"
			},
			new
			{
				Id = DiagnosticDescriptors.ProhibitSystemIoPath.Id,
				Title = DiagnosticDescriptors.ProhibitSystemIoPath.Title.ToString(),
				Category = DiagnosticDescriptors.ProhibitSystemIoPath.Category,
				Severity = DiagnosticDescriptors.ProhibitSystemIoPath.DefaultSeverity.ToString(),
				Description = DiagnosticDescriptors.ProhibitSystemIoPath.Description.ToString(),
				Remediation = "Use RaiPath or RaiRelPath with operator '/' (e.g. new RaiPath(a) / b)"
			},
			new
			{
				Id = DiagnosticDescriptors.EnforceWhitespaceFormatting.Id,
				Title = DiagnosticDescriptors.EnforceWhitespaceFormatting.Title.ToString(),
				Category = DiagnosticDescriptors.EnforceWhitespaceFormatting.Category,
				Severity = DiagnosticDescriptors.EnforceWhitespaceFormatting.DefaultSeverity.ToString(),
				Description = DiagnosticDescriptors.EnforceWhitespaceFormatting.Description.ToString(),
				Remediation = "Indent code with tabs ('\\t') and use single spaces between tokens"
			}
		};

		if (json)
		{
			var jsonOutput = System.Text.Json.JsonSerializer.Serialize(rules, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			Console.WriteLine(jsonOutput);
			return 0;
		}

		foreach (var rule in rules)
		{
			Messages.WriteInfo($"{Icons.Shield} [{rule.Id}] {rule.Title}");
			Console.WriteLine($"  Category:    {rule.Category}");
			Console.WriteLine($"  Severity:    {rule.Severity}");
			Console.WriteLine($"  Description: {rule.Description}");
			Console.WriteLine($"  Remedy:      {rule.Remediation}");
			Console.WriteLine();
		}

		return 0;
	}

	private static int RunCheck(string? targetPath, string? ruleFilter, bool json)
	{
		var files = ResolveSourceFiles(targetPath);
		if (files.Count == 0)
		{
			if (json)
			{
				Console.WriteLine("[]");
			}
			else
			{
				Messages.WriteWarning($"{Icons.Warning} No C# source files found to inspect.");
			}
			return 0;
		}

		var violations = new List<DiagnosticRecord>();

		foreach (var filePath in files)
		{
			var fileViolations = AnalyzeFile(filePath, ruleFilter);
			violations.AddRange(fileViolations);
		}

		if (json)
		{
			var jsonOutput = System.Text.Json.JsonSerializer.Serialize(violations, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			Console.WriteLine(jsonOutput);
			return violations.Count > 0 ? 1 : 0;
		}

		if (violations.Count == 0)
		{
			Messages.WriteSuccess($"{Icons.Success} All guardrails passed. Checked {files.Count} files.");
			return 0;
		}

		foreach (var v in violations)
		{
			Messages.WriteWarning($"{Icons.Warning} {v.File}({v.Line},{v.Column}): warning {v.RuleId}: {v.Message}");
		}

		Console.WriteLine();
		Messages.WriteError($"{Icons.Error} Found {violations.Count} violation(s) across {files.Count} file(s).");
		return 1;
	}

	private static int RunFix(string? targetPath, string? ruleFilter, bool dryRun, bool json)
	{
		var files = ResolveSourceFiles(targetPath);
		if (files.Count == 0)
		{
			Messages.WriteWarning($"{Icons.Warning} No C# source files found to fix.");
			return 0;
		}

		int totalFixes = 0;

		foreach (var filePath in files)
		{
			var code = File.ReadAllText(filePath);
			var tree = CSharpSyntaxTree.ParseText(code);
			var root = tree.GetRoot();

			var rewrites = root.DescendantNodes()
				.OfType<InvocationExpressionSyntax>()
				.Where(inv =>
				{
					if (inv.Expression is MemberAccessExpressionSyntax ma)
					{
						var isDirectory = (ruleFilter == null || ruleFilter.Equals("RAI001", StringComparison.OrdinalIgnoreCase))
							&& IsDirectoryTypeAccess(ma.Expression);
						var isFile = (ruleFilter == null || ruleFilter.Equals("RAI002", StringComparison.OrdinalIgnoreCase))
							&& IsFileTypeAccess(ma.Expression);
						var isPath = (ruleFilter == null || ruleFilter.Equals("RAI003", StringComparison.OrdinalIgnoreCase))
							&& IsPathTypeAccess(ma.Expression);

						if (isDirectory)
						{
							var method = ma.Name.Identifier.Text;
							var argCount = inv.ArgumentList.Arguments.Count;
							return method switch
							{
								"GetDirectories" => argCount >= 1,
								"EnumerateDirectories" => argCount >= 1,
								"GetFiles" => argCount >= 1,
								"EnumerateFiles" => argCount >= 1,
								_ => false
							};
						}

						if (isFile)
						{
							var method = ma.Name.Identifier.Text;
							var argCount = inv.ArgumentList.Arguments.Count;
							return method switch
							{
								"Exists" => argCount >= 1,
								"ReadAllLines" => argCount >= 1,
								"ReadAllText" => argCount >= 1,
								"WriteAllText" => argCount >= 2,
								"Delete" => argCount >= 1,
								_ => false
							};
						}

						if (isPath)
						{
							var method = ma.Name.Identifier.Text;
							return method == "Combine" && inv.ArgumentList.Arguments.Count >= 2;
						}
					}
					return false;
				})
				.ToList();

			if (rewrites.Count == 0)
			{
				continue;
			}

			totalFixes += rewrites.Count;

			if (dryRun)
			{
				Messages.WriteInfo($"{Icons.Info} [DryRun] Would fix {rewrites.Count} violation(s) in {filePath}");
				continue;
			}

			bool hasFileRewrite = false;

			var newRoot = root.ReplaceNodes(rewrites, (original, _) =>
			{
				var ma = (MemberAccessExpressionSyntax)original.Expression;

				if (IsDirectoryTypeAccess(ma.Expression))
				{
					hasFileRewrite = true;
					var method = ma.Name.Identifier.Text switch
					{
						"GetFiles" => "EnumerateFiles",
						"EnumerateFiles" => "EnumerateFiles",
						"GetDirectories" => "EnumerateDirectories",
						"EnumerateDirectories" => "EnumerateDirectories",
						var other => other
					};

					if ((method is "EnumerateDirectories" or "EnumerateFiles") && original.ArgumentList.Arguments.Count >= 1)
					{
						var pathArg = original.ArgumentList.Arguments[0];
						var remainingArgs = original.ArgumentList.Arguments.Skip(1);

						var objectCreation = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("RaiPath"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(pathArg.Expression))),
							null);

						var newMa = SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							objectCreation,
							SyntaxFactory.IdentifierName(method));

						return SyntaxFactory.InvocationExpression(
							newMa,
							SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(remainingArgs)))
							.WithTriviaFrom(original);
					}

					var fallbackMa = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName("RaiPath"),
						SyntaxFactory.IdentifierName(method)).WithTriviaFrom(ma);

					return original.WithExpression(fallbackMa);
				}

				if (IsPathTypeAccess(ma.Expression))
				{
					hasFileRewrite = true;
					var method = ma.Name.Identifier.Text;
					if (method == "Combine" && original.ArgumentList.Arguments.Count >= 2)
					{
						var args = original.ArgumentList.Arguments;
						ExpressionSyntax current = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("RaiPath"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(args[0].Expression))),
							null);

						for (int i = 1; i < args.Count; i++)
						{
							var slashToken = SyntaxFactory.Token(SyntaxKind.SlashToken)
								.WithLeadingTrivia(SyntaxFactory.Space)
								.WithTrailingTrivia(SyntaxFactory.Space);

							current = SyntaxFactory.BinaryExpression(
								SyntaxKind.DivideExpression,
								current,
								slashToken,
								args[i].Expression);
						}

						return current.WithTriviaFrom(original);
					}
				}

				if (IsFileTypeAccess(ma.Expression))
				{
					hasFileRewrite = true;
					var method = ma.Name.Identifier.Text;
					var pathArg = original.ArgumentList.Arguments[0];

					if (method == "Exists")
					{
						var objectCreation = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("RaiFile"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(pathArg.Expression))),
							null);

						var newMa = SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							objectCreation,
							SyntaxFactory.IdentifierName("Exists"));

						return SyntaxFactory.InvocationExpression(newMa, SyntaxFactory.ArgumentList())
							.WithTriviaFrom(original);
					}

					if (method == "Delete")
					{
						var objectCreation = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("RaiFile"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(pathArg.Expression))),
							null);

						var newMa = SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							objectCreation,
							SyntaxFactory.IdentifierName("rm"));

						return SyntaxFactory.InvocationExpression(newMa, SyntaxFactory.ArgumentList())
							.WithTriviaFrom(original);
					}

					if (method == "ReadAllLines")
					{
						var objectCreation = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("TextFile"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(pathArg.Expression))),
							null);

						var newMa = SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							objectCreation,
							SyntaxFactory.IdentifierName("Read"));

						return SyntaxFactory.InvocationExpression(newMa, SyntaxFactory.ArgumentList())
							.WithTriviaFrom(original);
					}

					if (method == "ReadAllText")
					{
						var objectCreation = SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space),
							SyntaxFactory.IdentifierName("TextFile"),
							SyntaxFactory.ArgumentList(
								SyntaxFactory.SingletonSeparatedList(
									SyntaxFactory.Argument(pathArg.Expression))),
							null);

						var newMa = SyntaxFactory.MemberAccessExpression(
							SyntaxKind.SimpleMemberAccessExpression,
							objectCreation,
							SyntaxFactory.IdentifierName("ReadAllText"));

						var remainingArgs = SyntaxFactory.ArgumentList(
							SyntaxFactory.SeparatedList(original.ArgumentList.Arguments.Skip(1)));

						return SyntaxFactory.InvocationExpression(newMa, remainingArgs)
							.WithTriviaFrom(original);
					}

					if (method == "WriteAllText")
					{
						var textArg = original.ArgumentList.Arguments[1];
						return SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.IdentifierName("TextFile"))
							.WithArgumentList(SyntaxFactory.ArgumentList(
								SyntaxFactory.SeparatedList(new[]
								{
									SyntaxFactory.Argument(pathArg.Expression),
									SyntaxFactory.Argument(textArg.Expression)
								})))
							.NormalizeWhitespace()
							.WithTriviaFrom(original);
					}
				}

				return original;
			});

			if (hasFileRewrite && !HasOsLibUsing(newRoot))
			{
				newRoot = AddOsLibUsing(newRoot);
			}

			File.WriteAllText(filePath, newRoot.ToFullString());
			Messages.WriteSuccess($"{Icons.Check} Fixed {rewrites.Count} violation(s) in {filePath}");
		}

		if (totalFixes == 0)
		{
			Messages.WriteSuccess($"{Icons.Success} Nothing to fix. All files conform to guardrails.");
			return 0;
		}

		if (dryRun)
		{
			Messages.WriteInfo($"{Icons.Info} Dry run complete. {totalFixes} fix(es) candidate(s) detected.");
		}
		else
		{
			Messages.WriteSuccess($"{Icons.Success} Applied {totalFixes} automatic code fix(es).");
		}

		return 0;
	}

	private static bool HasOsLibUsing(SyntaxNode root)
	{
		return root.DescendantNodes()
			.OfType<UsingDirectiveSyntax>()
			.Any(u => u.Name?.ToString() == "OsLib");
	}

	private static SyntaxNode AddOsLibUsing(SyntaxNode root)
	{
		if (root is not CompilationUnitSyntax compUnit)
		{
			return root;
		}

		var lineEnding = root.ToFullString().Contains("\r\n")
			? SyntaxFactory.CarriageReturnLineFeed
			: SyntaxFactory.LineFeed;

		if (compUnit.Usings.Count > 0)
		{
			var lastUsing = compUnit.Usings.Last();
			var trailingTrivia = lastUsing.GetTrailingTrivia();
			var singleEol = trailingTrivia.FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
			if (singleEol == default)
			{
				singleEol = lineEnding;
			}

			var updatedLastUsing = lastUsing.WithTrailingTrivia(singleEol);
			var osLibUsing = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("OsLib"))
				.NormalizeWhitespace()
				.WithTrailingTrivia(trailingTrivia);

			return compUnit
				.ReplaceNode(lastUsing, updatedLastUsing)
				.AddUsings(osLibUsing);
		}
		else
		{
			var osLibUsing = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("OsLib"))
				.NormalizeWhitespace()
				.WithTrailingTrivia(lineEnding);
			return compUnit.AddUsings(osLibUsing);
		}
	}

	private static bool IsDirectoryTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text == "Directory";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var text = ma.ToString();
			return text is "System.IO.Directory" or "IO.Directory";
		}
		return false;
	}

	private static bool IsFileTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text == "File";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var text = ma.ToString();
			return text is "System.IO.File" or "IO.File";
		}
		return false;
	}

	private static bool IsPathTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text == "Path";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var text = ma.ToString();
			return text is "System.IO.Path" or "IO.Path";
		}
		return false;
	}

	private static List<DiagnosticRecord> AnalyzeFile(string filePath, string? ruleFilter)
	{
		var records = new List<DiagnosticRecord>();
		var code = File.ReadAllText(filePath);
		var tree = CSharpSyntaxTree.ParseText(code);
		var root = tree.GetRoot();

		// Intercept System.IO.Directory / Directory and System.IO.File / File invocations
		var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

		foreach (var inv in invocations)
		{
			if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
			{
				if (IsDirectoryTypeAccess(memberAccess.Expression))
				{
					if (ruleFilter != null && !ruleFilter.Equals("RAI001", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					var lineSpan = memberAccess.Name.GetLocation().GetLineSpan();
					var line = lineSpan.StartLinePosition.Line + 1;
					var col = lineSpan.StartLinePosition.Character + 1;
					var methodName = memberAccess.Name.Identifier.Text;

					records.Add(new DiagnosticRecord(
						RuleId: "RAI001",
						Message: string.Format(DiagnosticDescriptors.ProhibitSystemIoDirectory.MessageFormat.ToString(), methodName),
						File: filePath,
						Line: line,
						Column: col));
				}
				else if (IsFileTypeAccess(memberAccess.Expression))
				{
					if (ruleFilter != null && !ruleFilter.Equals("RAI002", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					var lineSpan = memberAccess.Name.GetLocation().GetLineSpan();
					var line = lineSpan.StartLinePosition.Line + 1;
					var col = lineSpan.StartLinePosition.Character + 1;
					var methodName = memberAccess.Name.Identifier.Text;

					records.Add(new DiagnosticRecord(
						RuleId: "RAI002",
						Message: string.Format(DiagnosticDescriptors.ProhibitSystemIoFile.MessageFormat.ToString(), methodName),
						File: filePath,
						Line: line,
						Column: col));
				}
				else if (IsPathTypeAccess(memberAccess.Expression))
				{
					if (ruleFilter != null && !ruleFilter.Equals("RAI003", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					var lineSpan = memberAccess.Name.GetLocation().GetLineSpan();
					var line = lineSpan.StartLinePosition.Line + 1;
					var col = lineSpan.StartLinePosition.Character + 1;
					var methodName = memberAccess.Name.Identifier.Text;

					records.Add(new DiagnosticRecord(
						RuleId: "RAI003",
						Message: string.Format(DiagnosticDescriptors.ProhibitSystemIoPath.MessageFormat.ToString(), methodName),
						File: filePath,
						Line: line,
						Column: col));
				}
			}
		}

		if (ruleFilter == null || ruleFilter.Equals("RAI010", StringComparison.OrdinalIgnoreCase))
		{
			var text = tree.GetText();

			var immuneSpans = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
			foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
			{
				if (token.IsKind(SyntaxKind.StringLiteralToken) ||
					token.IsKind(SyntaxKind.CharacterLiteralToken) ||
					token.IsKind(SyntaxKind.InterpolatedStringTextToken) ||
					token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) ||
					token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) ||
					token.IsKind(SyntaxKind.Utf8StringLiteralToken) ||
					token.IsKind(SyntaxKind.Utf8SingleLineRawStringLiteralToken) ||
					token.IsKind(SyntaxKind.Utf8MultiLineRawStringLiteralToken))
				{
					immuneSpans.Add(token.Span);
				}
			}

			foreach (var line in text.Lines)
			{
				if (line.Span.Length == 0) continue;
				if (immuneSpans.Any(s => s.Start < line.Start && line.Start < s.End)) continue;

				var lineStr = line.ToString();
				int indentLen = 0;
				while (indentLen < lineStr.Length && (lineStr[indentLen] == ' ' || lineStr[indentLen] == '\t'))
				{
					indentLen++;
				}

				if (indentLen > 0)
				{
					var indent = lineStr.Substring(0, indentLen);
					if (indent.Contains(' '))
					{
						var lineNum = line.LineNumber + 1;
						var col = lineStr.IndexOf(' ') + 1;
						records.Add(new DiagnosticRecord(
							RuleId: "RAI010",
							Message: WhitespaceFormattingAnalyzer.TabIndentationMessage,
							File: filePath,
							Line: lineNum,
							Column: col));
					}
				}
			}

			var tokens = root.DescendantTokens(descendIntoTrivia: false)
				.Where(t => !t.IsKind(SyntaxKind.EndOfFileToken))
				.ToList();

			for (int i = 0; i < tokens.Count - 1; i++)
			{
				var tokenA = tokens[i];
				var tokenB = tokens[i + 1];

				var lineA = text.Lines.GetLineFromPosition(tokenA.Span.End).LineNumber;
				var lineB = text.Lines.GetLineFromPosition(tokenB.Span.Start).LineNumber;

				if (lineA == lineB)
				{
					var spanBetween = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(tokenA.Span.End, tokenB.Span.Start);
					if (spanBetween.Length >= 2)
					{
						var betweenText = text.ToString(spanBetween);
						int spaceIdx = betweenText.IndexOf("  ", StringComparison.Ordinal);
						if (spaceIdx >= 0)
						{
							var linePos = text.Lines.GetLinePosition(tokenA.Span.End + spaceIdx);
							records.Add(new DiagnosticRecord(
								RuleId: "RAI010",
								Message: WhitespaceFormattingAnalyzer.ConsecutiveSpacesMessage,
								File: filePath,
								Line: linePos.Line + 1,
								Column: linePos.Character + 1));
						}
					}
				}
			}
		}

		return records;
	}

	private static List<string> ResolveSourceFiles(string? path)
	{
		var result = new List<string>();
		var target = string.IsNullOrWhiteSpace(path) ? Directory.GetCurrentDirectory() : Path.GetFullPath(path);

		if (File.Exists(target) && target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
		{
			result.Add(target);
			return result;
		}

		if (Directory.Exists(target))
		{
			foreach (var file in Directory.EnumerateFiles(target, "*.cs", SearchOption.AllDirectories))
			{
				if (file.Contains("/obj/") || file.Contains("/bin/") || file.Contains("/.git/"))
				{
					continue;
				}
				result.Add(file);
			}
		}

		return result;
	}

	private static int RunUnknownCommand(string command)
	{
		Messages.WriteError($"Unknown command '{command}'. See 'rail --help'.");
		return 1;
	}

	internal static string GetVersion()
	{
		var assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
		var name = "rail";
		var version = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion
			.Split('+')[0]
			?? assembly.GetName().Version?.ToString()
			?? "4.5.8";
		return $"{name} v{version}";
	}

	private static string? ParamValue(string[] options, params string[] aliases)
		=> aliases.Select(a => Array.IndexOf(options, a)).Where(i => i >= 0)
			.Select(i => i + 1 < options.Length && !options[i + 1].StartsWith("-")
				? options[i + 1]
				: throw new ArgumentException($"The option '{options[i]}' requires a value."))
			.FirstOrDefault();

	private static bool HasOption(string[] options, params string[] aliases)
		=> aliases.Any(options.Contains);

	private static string? PositionalArg(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			if (SwitchesWithValues.Contains(args[i]))
			{
				i++;
				continue;
			}
			if (args[i].StartsWith("-")) continue;
			return args[i];
		}
		return null;
	}
}

public sealed record DiagnosticRecord(string RuleId, string Message, string File, int Line, int Column);
