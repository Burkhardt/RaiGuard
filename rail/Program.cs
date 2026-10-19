using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OsLib;
using RaiGuard.Core.Diagnostics;
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
	public static string? TargetGroup { get; set; }

	public static string[] Help =>
	[
		$"Commands:\t{Icons.Info}\tcheck, fix, rules, groups",
		$"  rail check [<path>] [--rule <id>] [--group <name>] [--json]",
		$"  rail fix [<path>] [--rule <id>] [--group <name>] [--dry-run]",
		$"  rail rules [--rule <id>] [--group <name>] [--groups] [--json]",
		$"  rail groups [--group <name>] [--json]",
		$"-h, --help\t{Icons.Help}\tprint out all options",
		$"-v, --version\t{Icons.Info}\tprint version info",
		$"-n, --nologo\t{(Banner ? Icons.Banner : Icons.NoBanner)}\tdo not display the banner",
		$"-b, --debug\t{(Debug ? Icons.Bug : Icons.Runner)}\tenable debug output",
		$"-p, --path\t{Icons.Folder}\t{PathDescription()}",
		$"--rule\t\t{Icons.Shield}\t{RuleDescription()}",
		$"-g, --group\t{Icons.Shield}\t{GroupDescription()}",
		$"--groups\t{Icons.Shield}\tlist all rule groups (sets) with their member RAIxxx rules",
		$"--json\t\t{(Json ? Icons.Success : Icons.NotAvailable)}\toutput diagnostics in JSON format",
		$"--dry-run\t{Icons.Info}\tsimulate code fixes without modifying source files on disk",
		$"{Icons.Info} Rules\t{Icons.Shield}\tRAI001..RAI016 across sets: OsLib, CloudSync, JsonPit, CliCommand, RaiMedia, Formatting",
	];

	private static string PathDescription() =>
		!string.IsNullOrWhiteSpace(TargetPath) ? TargetPath : "target file or directory (defaults to current directory)";

	private static string RuleDescription() =>
		!string.IsNullOrWhiteSpace(TargetRule) ? TargetRule : "filter by rule ID (e.g. RAI001, RAI011)";

	private static string GroupDescription() =>
		!string.IsNullOrWhiteSpace(TargetGroup) ? TargetGroup : "filter by rule group set (OsLib, CloudSync, JsonPit, CliCommand, RaiMedia, Formatting)";

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
	private static readonly string[] Commands = ["check", "fix", "rules", "groups"];
	private static readonly string[] SwitchesWithValues = ["-p", "--path", "--rule", "-g", "--group"];

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
		bool listGroups = HasOption(args, "--groups");

		var pathParam = ParamValue(args, "-p", "--path") ?? (command == "groups" ? null : PositionalArg(args));
		var ruleParam = ParamValue(args, "--rule");
		var groupParam = ParamValue(args, "-g", "--group") ?? (command == "groups" ? PositionalArg(args) : null);

		Messages.TargetPath = pathParam;
		Messages.TargetRule = ruleParam;
		Messages.TargetGroup = groupParam;

		if (Messages.Banner && !Messages.Json)
		{
			Messages.WriteBanner(BannerTitle);
		}

		if (listGroups || command == "groups")
		{
			return RunGroups(groupParam, Messages.Json);
		}

		return command switch
		{
			"check" => RunCheck(pathParam, ruleParam, groupParam, Messages.Json),
			"fix" => RunFix(pathParam, ruleParam, groupParam, dryRun, Messages.Json),
			"rules" => RunRules(ruleParam, groupParam, Messages.Json),
			_ => RunUnknownCommand(command)
		};
	}

	private static int RunMappedArguments(string[] args)
	{
		Messages.Debug = HasOption(args, "-b", "-d", "--debug");
		Messages.Banner = !HasOption(args, "-n", "-l", "--nologo");
		Messages.Json = HasOption(args, "--json");
		bool listGroups = HasOption(args, "--groups");

		var pathParam = ParamValue(args, "-p", "--path") ?? PositionalArg(args);
		var ruleParam = ParamValue(args, "--rule");
		var groupParam = ParamValue(args, "-g", "--group");

		if (Messages.Banner && !Messages.Json)
		{
			Messages.WriteBanner(BannerTitle);
		}

		if (listGroups)
		{
			return RunGroups(groupParam, Messages.Json);
		}

		return RunCheck(pathParam, ruleParam, groupParam, Messages.Json);
	}

	private static int RunRules(string? ruleFilter, string? groupFilter, bool json)
	{
		var filterSet = DiagnosticDescriptors.ResolveFilterSet(ruleFilter, groupFilter);
		var rules = DiagnosticDescriptors.Catalog
			.Where(entry => filterSet == null || filterSet.Contains(entry.Id))
			.Select(entry => new
			{
				Id = entry.Id,
				Title = entry.Descriptor.Title.ToString(),
				Groups = entry.Groups.ToArray(),
				Category = entry.Descriptor.Category,
				Severity = entry.Descriptor.DefaultSeverity.ToString(),
				Description = entry.Descriptor.Description.ToString(),
				Remediation = entry.Remediation
			})
			.ToArray();

		if (json)
		{
			var jsonOutput = System.Text.Json.JsonSerializer.Serialize(rules, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			Console.WriteLine(jsonOutput);
			return 0;
		}

		foreach (var rule in rules)
		{
			Messages.WriteInfo($"{Icons.Shield} [{rule.Id}] {rule.Title}");
			Console.WriteLine($"  Groups:      {string.Join(", ", rule.Groups)}");
			Console.WriteLine($"  Category:    {rule.Category}");
			Console.WriteLine($"  Severity:    {rule.Severity}");
			Console.WriteLine($"  Description: {rule.Description}");
			Console.WriteLine($"  Remedy:      {rule.Remediation}");
			Console.WriteLine();
		}

		return 0;
	}

	private static int RunGroups(string? groupFilter, bool json)
	{
		var requestedGroups = string.IsNullOrWhiteSpace(groupFilter)
			? null
			: new HashSet<string>(
				groupFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
				StringComparer.OrdinalIgnoreCase);

		var catalogById = DiagnosticDescriptors.Catalog.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

		var groups = DiagnosticDescriptors.Groups
			.Where(g => requestedGroups == null || requestedGroups.Contains(g.Name))
			.Select(g => new
			{
				Group = g.Name,
				Description = g.Description,
				RuleIds = g.RuleIds.ToArray(),
				Rules = g.RuleIds
					.Where(id => catalogById.ContainsKey(id))
					.Select(id => new
					{
						Id = id,
						Title = catalogById[id].Descriptor.Title.ToString(),
						Severity = catalogById[id].Descriptor.DefaultSeverity.ToString()
					})
					.ToArray()
			})
			.ToArray();

		if (json)
		{
			var jsonOutput = System.Text.Json.JsonSerializer.Serialize(groups, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
			Console.WriteLine(jsonOutput);
			return 0;
		}

		foreach (var g in groups)
		{
			Messages.WriteInfo($"{Icons.Shield} Group '{g.Group}' ({{ {string.Join(", ", g.RuleIds)} }})");
			Console.WriteLine($"  Description: {g.Description}");
			foreach (var r in g.Rules)
			{
				Console.WriteLine($"    - [{r.Id}] {r.Title}");
			}
			Console.WriteLine();
		}

		return 0;
	}

	private static int RunCheck(string? targetPath, string? ruleFilter, string? groupFilter, bool json)
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
			var fileViolations = AnalyzeFile(filePath, ruleFilter, groupFilter);
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

	private static int RunFix(string? targetPath, string? ruleFilter, string? groupFilter, bool dryRun, bool json)
	{
		var files = ResolveSourceFiles(targetPath);
		if (files.Count == 0)
		{
			Messages.WriteWarning($"{Icons.Warning} No C# source files found to fix.");
			return 0;
		}

		var filterSet = DiagnosticDescriptors.ResolveFilterSet(ruleFilter, groupFilter);
		int totalFixes = 0;

		foreach (var filePath in files)
		{
			var code = File.ReadAllText(filePath);

			// Optional explicit RAI010 tab indentation fix when --rule RAI010 or --group Formatting is passed
			if (filterSet != null && filterSet.Count == 1 && filterSet.Contains("RAI010"))
			{
				var (tabFixedCode, tabFixes) = NormalizeIndentationToTabs(code);
				if (tabFixes > 0)
				{
					totalFixes += tabFixes;
					if (dryRun)
					{
						Messages.WriteInfo($"{Icons.Info} [DryRun] Would fix {tabFixes} violation(s) in {filePath}");
					}
					else
					{
						File.WriteAllText(filePath, tabFixedCode);
						Messages.WriteSuccess($"{Icons.Check} Fixed {tabFixes} violation(s) in {filePath}");
					}
				}
				continue;
			}

			var tree = CSharpSyntaxTree.ParseText(code);
			var root = tree.GetRoot();

			bool allowRai001 = filterSet == null || filterSet.Contains("RAI001");
			bool allowRai002 = filterSet == null || filterSet.Contains("RAI002");
			bool allowRai003 = filterSet == null || filterSet.Contains("RAI003");
			bool allowRai004 = filterSet == null || filterSet.Contains("RAI004");
			bool allowRai013 = filterSet == null || filterSet.Contains("RAI013");
			bool allowRai016 = filterSet == null || filterSet.Contains("RAI016");

			var rewrites = root.DescendantNodes()
				.Where(node =>
				{
					if (node is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax ma)
					{
						if (allowRai001 && RaiKeepArchitectureAnalyzer.IsDirectoryTypeAccess(ma.Expression))
						{
							var method = ma.Name.Identifier.Text;
							var argCount = inv.ArgumentList.Arguments.Count;
							return method is "GetDirectories" or "EnumerateDirectories" or "GetFiles" or "EnumerateFiles" && argCount >= 1;
						}

						if (allowRai002 && RaiKeepArchitectureAnalyzer.IsFileTypeAccess(ma.Expression))
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

						if (allowRai003 && RaiKeepArchitectureAnalyzer.IsPathTypeAccess(ma.Expression))
						{
							var method = ma.Name.Identifier.Text;
							return method == "Combine" && inv.ArgumentList.Arguments.Count >= 2;
						}

						if (allowRai013 &&
							ma.Name.Identifier.Text == "Exists" &&
							inv.ArgumentList.Arguments.Count == 0 &&
							ma.Expression is MemberAccessExpressionSyntax innerMa &&
							innerMa.Name.Identifier.Text == "PitDir")
						{
							return true;
						}
					}

					if (allowRai004 && node is ObjectCreationExpressionSyntax creation)
					{
						var typeName = creation.Type is IdentifierNameSyntax id ? id.Identifier.Text : creation.Type.ToString();
						if (typeName == "RaiPath" &&
							creation.ArgumentList?.Arguments.Count == 1 &&
							creation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit &&
							lit.IsKind(SyntaxKind.StringLiteralExpression))
						{
							var val = lit.Token.ValueText;
							return !string.IsNullOrEmpty(val) &&
								val != "~" &&
								val != "." &&
								val != ".." &&
								!val.EndsWith("/", StringComparison.Ordinal) &&
								!val.EndsWith("\\", StringComparison.Ordinal);
						}
					}

					if (allowRai016 && node is MemberAccessExpressionSyntax member &&
						member.ToString() == "ImageNamingConvention.Legacy")
					{
						return true;
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

			bool hasOsLibRewrite = false;

			var newRoot = root.ReplaceNodes(rewrites, (original, _) =>
			{
				if (original is ObjectCreationExpressionSyntax creation &&
					creation.ArgumentList?.Arguments.Count == 1 &&
					creation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit)
				{
					var newVal = lit.Token.ValueText + "/";
					var newLit = SyntaxFactory.LiteralExpression(
						SyntaxKind.StringLiteralExpression,
						SyntaxFactory.Literal(newVal)).WithTriviaFrom(lit);
					var newArg = creation.ArgumentList.Arguments[0].WithExpression(newLit);
					return creation.WithArgumentList(
						SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(newArg)))
						.WithTriviaFrom(creation);
				}

				if (original is MemberAccessExpressionSyntax legacyMa &&
					legacyMa.ToString() == "ImageNamingConvention.Legacy")
				{
					return legacyMa.WithName(SyntaxFactory.IdentifierName("Structured")).WithTriviaFrom(legacyMa);
				}

				if (original is not InvocationExpressionSyntax inv || inv.Expression is not MemberAccessExpressionSyntax ma)
				{
					return original;
				}

				if (ma.Name.Identifier.Text == "Exists" &&
					inv.ArgumentList.Arguments.Count == 0 &&
					ma.Expression is MemberAccessExpressionSyntax innerPitDir &&
					innerPitDir.Name.Identifier.Text == "PitDir")
				{
					var jsonFileMa = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						innerPitDir.Expression,
						SyntaxFactory.IdentifierName("JsonFile"));
					var pathMa = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						jsonFileMa,
						SyntaxFactory.IdentifierName("Path"));
					var existsMa = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						pathMa,
						SyntaxFactory.IdentifierName("Exists"));
					return inv.WithExpression(existsMa).WithTriviaFrom(inv);
				}

				if (RaiKeepArchitectureAnalyzer.IsDirectoryTypeAccess(ma.Expression))
				{
					hasOsLibRewrite = true;
					var method = ma.Name.Identifier.Text switch
					{
						"GetFiles" => "EnumerateFiles",
						"EnumerateFiles" => "EnumerateFiles",
						"GetDirectories" => "EnumerateDirectories",
						"EnumerateDirectories" => "EnumerateDirectories",
						var other => other
					};

					if ((method is "EnumerateDirectories" or "EnumerateFiles") && inv.ArgumentList.Arguments.Count >= 1)
					{
						var pathArg = inv.ArgumentList.Arguments[0];
						var remainingArgs = inv.ArgumentList.Arguments.Skip(1);

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
							.WithTriviaFrom(inv);
					}

					var fallbackMa = SyntaxFactory.MemberAccessExpression(
						SyntaxKind.SimpleMemberAccessExpression,
						SyntaxFactory.IdentifierName("RaiPath"),
						SyntaxFactory.IdentifierName(method)).WithTriviaFrom(ma);

					return inv.WithExpression(fallbackMa);
				}

				if (RaiKeepArchitectureAnalyzer.IsPathTypeAccess(ma.Expression))
				{
					hasOsLibRewrite = true;
					var method = ma.Name.Identifier.Text;
					if (method == "Combine" && inv.ArgumentList.Arguments.Count >= 2)
					{
						var args = inv.ArgumentList.Arguments;
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

						return current.WithTriviaFrom(inv);
					}
				}

				if (RaiKeepArchitectureAnalyzer.IsFileTypeAccess(ma.Expression))
				{
					hasOsLibRewrite = true;
					var method = ma.Name.Identifier.Text;
					var pathArg = inv.ArgumentList.Arguments[0];

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
							.WithTriviaFrom(inv);
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
							.WithTriviaFrom(inv);
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
							.WithTriviaFrom(inv);
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
							SyntaxFactory.SeparatedList(inv.ArgumentList.Arguments.Skip(1)));

						return SyntaxFactory.InvocationExpression(newMa, remainingArgs)
							.WithTriviaFrom(inv);
					}

					if (method == "WriteAllText")
					{
						var textArg = inv.ArgumentList.Arguments[1];
						return SyntaxFactory.ObjectCreationExpression(
							SyntaxFactory.IdentifierName("TextFile"))
							.WithArgumentList(SyntaxFactory.ArgumentList(
								SyntaxFactory.SeparatedList(new[]
								{
									SyntaxFactory.Argument(pathArg.Expression),
									SyntaxFactory.Argument(textArg.Expression)
								})))
							.NormalizeWhitespace()
							.WithTriviaFrom(inv);
					}
				}

				return original;
			});

			if (hasOsLibRewrite && !HasOsLibUsing(newRoot))
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

	private static (string Code, int Fixes) NormalizeIndentationToTabs(string code)
	{
		var sb = new StringBuilder(code.Length);
		int fixes = 0;
		int i = 0;
		bool atLineStart = true;

		while (i < code.Length)
		{
			if (atLineStart)
			{
				int indentStart = i;
				while (i < code.Length && (code[i] == ' ' || code[i] == '\t'))
				{
					i++;
				}

				if (i > indentStart)
				{
					var indent = code.Substring(indentStart, i - indentStart);
					if (indent.Contains(' '))
					{
						fixes++;
						int spaceCount = 0;
						foreach (char c in indent)
						{
							if (c == '\t')
							{
								sb.Append('\t');
								spaceCount = 0;
							}
							else if (c == ' ')
							{
								spaceCount++;
								if (spaceCount == 4)
								{
									sb.Append('\t');
									spaceCount = 0;
								}
							}
						}
						if (spaceCount > 0)
						{
							sb.Append('\t');
						}
					}
					else
					{
						sb.Append(indent);
					}
				}
				atLineStart = false;
			}
			else
			{
				char c = code[i++];
				sb.Append(c);
				if (c == '\n')
				{
					atLineStart = true;
				}
			}
		}

		return (sb.ToString(), fixes);
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

	private static List<DiagnosticRecord> AnalyzeFile(string filePath, string? ruleFilter, string? groupFilter = null)
	{
		var code = File.ReadAllText(filePath);
		var tree = CSharpSyntaxTree.ParseText(code, path: filePath);
		var findings = RaiKeepArchitectureAnalyzer.AnalyzeSyntaxRoot(tree, ruleFilter, includeSystemIoAndFormatting: true, groupFilter: groupFilter);

		return findings
			.Select(f => new DiagnosticRecord(
				RuleId: f.RuleId,
				Message: f.Message,
				File: filePath,
				Line: f.Line,
				Column: f.Column))
			.ToList();
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
			?? "4.5.10";
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
