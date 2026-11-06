namespace RaiGuard;

internal sealed class RailArguments
{
	internal string Command { get; private set; } = "check";
	internal List<string> Paths { get; } = [];
	internal string? Rule { get; private set; }
	internal string? Group { get; private set; }
	internal bool Json { get; private set; }
	internal bool Debug { get; private set; }
	internal bool NoLogo { get; private set; }
	internal bool DryRun { get; private set; }
	internal bool Groups { get; private set; }
	internal bool Help { get; private set; }
	internal bool Version { get; private set; }

	internal static string CommandName(string[] args) => args.Length > 0 && new[] { "check", "fix", "rules", "groups" }.Contains(args[0], StringComparer.OrdinalIgnoreCase)
		? args[0].ToLowerInvariant() : "check";

	// Used only when parsing fails, so even an option later in the invocation
	// retains the machine-output contract. Tokens after -- are never options.
	internal static bool RequestsJson(string[] args) => args.TakeWhile(a => a != "--").Contains("--json");

	internal static RailArguments Parse(string[] args)
	{
		var result = new RailArguments();
		var start = 0;
		if (args.Length > 0 && new[] { "check", "fix", "rules", "groups" }.Contains(args[0], StringComparer.OrdinalIgnoreCase))
		{
			result.Command = args[0].ToLowerInvariant();
			start = 1;
		}
		var positionalOnly = false;
		for (var i = start; i < args.Length; i++)
		{
			var token = args[i];
			if (positionalOnly) { result.Paths.Add(token); continue; }
			if (token == "--") { positionalOnly = true; continue; }
			string Value()
			{
				if (i + 1 >= args.Length || args[i + 1].StartsWith('-') || string.IsNullOrWhiteSpace(args[i + 1]))
					throw new ArgumentException($"The option '{token}' requires a value.");
				return args[++i];
			}
			switch (token)
			{
				case "-p": case "--path": result.Paths.Add(Value()); break;
				case "--rule": result.Rule = Join(result.Rule, Value()); break;
				case "-g": case "--group": result.Group = Join(result.Group, Value()); break;
				case "--json": result.Json = true; break;
				case "-b": case "-d": case "--debug": result.Debug = true; break;
				case "-n": case "-l": case "--nologo": result.NoLogo = true; break;
				case "--dry-run": result.DryRun = true; break;
				case "--groups": result.Groups = true; break;
				case "-h": case "--help": result.Help = true; break;
				case "-v": case "--version": result.Version = true; break;
				default:
					if (token.StartsWith('-')) throw new ArgumentException($"Unknown option '{token}'. See 'rail --help'.");
					if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A source path must not be empty.");
					result.Paths.Add(token);
					break;
			}
		}
		if (result.Command == "groups" && result.Paths.Count == 1)
		{
			result.Group ??= result.Paths[0];
			result.Paths.Clear();
		}
		if ((result.Command is "groups" or "rules" || result.Groups) && result.Paths.Count > 0)
			throw new ArgumentException($"'{result.Command}' does not accept source paths.");
		return result;
	}

	private static string Join(string? previous, string next) => previous == null ? next : previous + "," + next;
}
