using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RaiGuard.Core.Rules;

/// <summary>
/// Source-level suppressions honoured by both the Roslyn analyzer and the standalone <c>rail</c> scanner:
/// <list type="bullet">
/// <item><c>#pragma warning disable RAI015</c> … <c>#pragma warning restore RAI015</c> (bare <c>disable</c> covers every rule).</item>
/// <item><c>// rail:ignore RAI015</c> (or several ids, or none for all rules) as a trailing comment on the flagged line,
/// or on its own line directly above it.</item>
/// <item><c>// rail:ignore-file RAI015</c> anywhere in the file suppresses the listed rules (or all) for the whole file.</item>
/// </list>
/// </summary>
public sealed class SuppressionMap
{
	private static readonly Regex IgnoreComment = new(
		@"rail:ignore(?<file>-file)?\b(?<ids>[^\r\n]*)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex RuleId = new(
		@"\bRAI\d{3}\b",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly List<(int Position, bool Disable, HashSet<string>? Ids)> _pragmas = new();
	private readonly Dictionary<int, HashSet<string>?> _lineIgnores = new();
	private HashSet<string>? _fileIgnores;
	private bool _fileIgnoresAll;

	public static SuppressionMap Build(SyntaxNode root)
	{
		var map = new SuppressionMap();
		var text = root.SyntaxTree.GetText();

		foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
		{
			if (trivia.GetStructure() is PragmaWarningDirectiveTriviaSyntax pragma)
			{
				var ids = pragma.ErrorCodes.Count == 0
					? null
					: new HashSet<string>(
						pragma.ErrorCodes.Select(c => c.ToString().Trim()),
						StringComparer.OrdinalIgnoreCase);
				map._pragmas.Add((
					pragma.SpanStart,
					pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword),
					ids));
				continue;
			}

			if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
				!trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
			{
				continue;
			}

			var match = IgnoreComment.Match(trivia.ToString());
			if (!match.Success)
			{
				continue;
			}

			var parsed = RuleId.Matches(match.Groups["ids"].Value)
				.Cast<Match>()
				.Select(m => m.Value.ToUpperInvariant())
				.ToList();
			var idSet = parsed.Count == 0 ? null : new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);

			if (match.Groups["file"].Success)
			{
				if (idSet == null)
				{
					map._fileIgnoresAll = true;
				}
				else
				{
					map._fileIgnores ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					map._fileIgnores.UnionWith(idSet);
				}
				continue;
			}

			var commentLine = text.Lines.GetLineFromPosition(trivia.SpanStart);
			var lineNumber = commentLine.LineNumber + 1;
			var leading = text.ToString(TextSpan.FromBounds(commentLine.Start, trivia.SpanStart));
			bool ownLine = string.IsNullOrWhiteSpace(leading);

			map.AddLineIgnore(lineNumber, idSet);
			if (ownLine)
			{
				map.AddLineIgnore(lineNumber + 1, idSet);
			}
		}

		return map;
	}

	public bool IsSuppressed(string ruleId, int position, int line)
	{
		if (_fileIgnoresAll || (_fileIgnores?.Contains(ruleId) ?? false))
		{
			return true;
		}

		if (_lineIgnores.TryGetValue(line, out var ids) && (ids == null || ids.Contains(ruleId)))
		{
			return true;
		}

		bool disabled = false;
		foreach (var (pos, disable, pragmaIds) in _pragmas)
		{
			if (pos > position)
			{
				break;
			}
			if (pragmaIds == null || pragmaIds.Contains(ruleId))
			{
				disabled = disable;
			}
		}
		return disabled;
	}

	private void AddLineIgnore(int line, HashSet<string>? ids)
	{
		if (_lineIgnores.TryGetValue(line, out var existing))
		{
			if (existing == null)
			{
				return;
			}
			if (ids == null)
			{
				_lineIgnores[line] = null;
				return;
			}
			existing.UnionWith(ids);
			return;
		}
		_lineIgnores[line] = ids == null ? null : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
	}
}
