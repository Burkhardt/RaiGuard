using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Core.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WhitespaceFormattingAnalyzer : DiagnosticAnalyzer
{
	public const string TabIndentationMessage = "Indentation must use tabs ('\\t'), not spaces. Please reformat using an approved formatter.";
	public const string ConsecutiveSpacesMessage = "Multiple consecutive spaces are prohibited in code outside string literals.";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DiagnosticDescriptors.EnforceWhitespaceFormatting);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
	}

	private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
	{
		var tree = context.Tree;
		var text = tree.GetText(context.CancellationToken);
		var root = tree.GetRoot(context.CancellationToken);

		// Collect string and character literal spans for immunity against multiline indentation checks
		var immuneSpans = new List<TextSpan>();
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

		// a) Check leading line whitespace (indentation)
		foreach (var line in text.Lines)
		{
			if (line.Span.Length == 0)
			{
				continue;
			}

			// If the line start is inside a multi-line string literal, skip indentation check
			if (immuneSpans.Any(s => s.Start < line.Start && line.Start < s.End))
			{
				continue;
			}

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
					var span = new TextSpan(line.Start, indentLen);
					var diagnostic = Diagnostic.Create(
						DiagnosticDescriptors.EnforceWhitespaceFormatting,
						Location.Create(tree, span),
						TabIndentationMessage);
					context.ReportDiagnostic(diagnostic);
				}
			}
		}

		// b) Check inter-token whitespace outside string/char literals
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
				var spanBetween = TextSpan.FromBounds(tokenA.Span.End, tokenB.Span.Start);
				if (spanBetween.Length >= 2)
				{
					var betweenText = text.ToString(spanBetween);
					int spaceIndex = betweenText.IndexOf("  ", StringComparison.Ordinal);
					if (spaceIndex >= 0)
					{
						var diagnosticSpan = new TextSpan(tokenA.Span.End + spaceIndex, 2);
						var diagnostic = Diagnostic.Create(
							DiagnosticDescriptors.EnforceWhitespaceFormatting,
							Location.Create(tree, diagnosticSpan),
							ConsecutiveSpacesMessage);
						context.ReportDiagnostic(diagnostic);
					}
				}
			}
		}
	}
}
