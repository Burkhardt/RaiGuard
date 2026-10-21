using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using RaiGuard.Core.Diagnostics;

namespace RaiGuard.Core.Rules;

public sealed class SyntaxDiagnosticFinding
{
	public string RuleId { get; }
	public DiagnosticDescriptor Descriptor { get; }
	public string Message { get; }
	public object[] MessageArgs { get; }
	public Location Location { get; }
	public int Line { get; }
	public int Column { get; }

	public SyntaxDiagnosticFinding(
		string ruleId,
		DiagnosticDescriptor descriptor,
		string message,
		object[] messageArgs,
		Location location,
		int line,
		int column)
	{
		RuleId = ruleId;
		Descriptor = descriptor;
		Message = message;
		MessageArgs = messageArgs;
		Location = location;
		Line = line;
		Column = column;
	}
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RaiKeepArchitectureAnalyzer : DiagnosticAnalyzer
{
	private static readonly HashSet<string> OsLibFileTypes = new(StringComparer.Ordinal)
	{
		"RaiPath", "RaiRelPath", "RaiFile", "TextFile", "ImageFile",
		"ImageTreeFile", "ItemTreeTextFile", "CanonicalFile", "PitFile",
		"CsvFile", "TmpFile", "RaiZipFile"
	};

	private static readonly HashSet<string> DiagramBuilderTypes = new(StringComparer.Ordinal)
	{
		"DiagramBuilder",
		"OneUseCaseDiagramBuilder",
		"RoleFillerDiagramBuilder",
		"ClassDiagramBuilder",
		"ActivityDiagramBuilder",
		"SequenceDiagramBuilder",
		"DistributionDiagramBuilder"
	};

	private static readonly Dictionary<string, string> TypedCliWrappers = new(StringComparer.OrdinalIgnoreCase)
	{
		["pits"] = "PitsCommand",
		["pitseeder"] = "PitsCommand",
		["iorg"] = "IorgCommand",
		["imgseeder"] = "IorgCommand",
		["raid"] = "RaidCommand",
		["raidseeder"] = "RaidCommand",
		["amafu"] = "AmafuCommand",
		["jpit"] = "JpitCommand",
		["deno"] = "DenoCommand",
		["rclone"] = "RCloneCommand",
		["unzip"] = "UnzipCommand",
		["magick"] = "ImageMagickCommand",
		["plantuml"] = "PlantUmlCommand",
		["optipng"] = "OptiPngCommand",
		["jpegtran"] = "JpegTranCommand"
	};

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		DiagnosticDescriptors.ProhibitRaiPathWithoutTrailingSlash,
		DiagnosticDescriptors.ProhibitPathStringConcatenation,
		DiagnosticDescriptors.ProhibitUntypedProcessExecution,
		DiagnosticDescriptors.ProhibitEnvironmentConfigBypass,
		DiagnosticDescriptors.EnforceCloudInPlaceInvariant,
		DiagnosticDescriptors.ProhibitInvertedCopyMoveReceiver,
		DiagnosticDescriptors.ProhibitPitReadModifyWrite,
		DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
		DiagnosticDescriptors.ProhibitPitDirExistsAndReloadHazards,
		DiagnosticDescriptors.EnforcePitInstanceDisposal,
		DiagnosticDescriptors.ProhibitInvalidPitIdAndUnsafeJsonParse,
		DiagnosticDescriptors.EnforceRaiUtilsAndDiagramConventions
	);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxTreeAction(AnalyzeSyntaxTreeContext);
	}

	private static void AnalyzeSyntaxTreeContext(SyntaxTreeAnalysisContext context)
	{
		var findings = AnalyzeSyntaxRoot(context.Tree, ruleFilter: null, includeSystemIoAndFormatting: false);
		foreach (var finding in findings)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				finding.Descriptor,
				finding.Location,
				finding.MessageArgs));
		}
	}

	public static List<SyntaxDiagnosticFinding> AnalyzeSyntaxRoot(
		SyntaxTree tree,
		string? ruleFilter = null,
		bool includeSystemIoAndFormatting = true,
		string? groupFilter = null)
	{
		var findings = new List<SyntaxDiagnosticFinding>();
		var root = tree.GetRoot();
		var filterSet = DiagnosticDescriptors.ResolveFilterSet(ruleFilter, groupFilter);

		bool MatchesRule(string id) =>
			filterSet == null || filterSet.Contains(id);

		void AddFinding(DiagnosticDescriptor descriptor, Location location, params object[] messageArgs)
		{
			if (!MatchesRule(descriptor.Id))
			{
				return;
			}

			var lineSpan = location.GetLineSpan();
			var line = lineSpan.StartLinePosition.Line + 1;
			var col = lineSpan.StartLinePosition.Character + 1;
			var formatted = messageArgs.Length == 0
				? descriptor.MessageFormat.ToString()
				: string.Format(descriptor.MessageFormat.ToString(), messageArgs);

			findings.Add(new SyntaxDiagnosticFinding(
				descriptor.Id,
				descriptor,
				formatted,
				messageArgs,
				location,
				line,
				col));
		}

		// 1. System.IO Rules (RAI001, RAI002, RAI003) when running via CLI syntax engine
		if (includeSystemIoAndFormatting)
		{
			AnalyzeSystemIoSyntax(root, MatchesRule, AddFinding);
		}

		// 2. Object Creation Rules (RAI004, RAI005, RAI006, RAI015, RAI016)
		foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
		{
			var typeName = GetSimpleTypeName(creation.Type);
			var args = creation.ArgumentList?.Arguments ?? default;

			// RAI004: new RaiPath("...") without trailing slash
			if (typeName == "RaiPath" && args.Count == 1 && MatchesRule("RAI004"))
			{
				if (args[0].Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
				{
					var val = lit.Token.ValueText;
					if (!string.IsNullOrEmpty(val) &&
						val != "~" &&
						val != "." &&
						val != ".." &&
						!val.EndsWith("/", StringComparison.Ordinal) &&
						!val.EndsWith("\\", StringComparison.Ordinal))
					{
						AddFinding(DiagnosticDescriptors.ProhibitRaiPathWithoutTrailingSlash, args[0].GetLocation(), val);
					}
				}
			}

			// RAI005: String concatenation inside OsLib file/path constructors
			if (OsLibFileTypes.Contains(typeName) && args.Count >= 1 && MatchesRule("RAI005"))
			{
				foreach (var arg in args)
				{
					if (ContainsPathConcatenation(arg.Expression))
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitPathStringConcatenation,
							arg.GetLocation(),
							arg.Expression.ToString(),
							typeName);
					}
				}
			}

			// RAI006: new Process(), new ProcessStartInfo(), or new RaiSystem("pits"|...)
			if (MatchesRule("RAI006"))
			{
				if (typeName is "Process" or "ProcessStartInfo")
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitUntypedProcessExecution,
						creation.Type.GetLocation(),
						$"new {typeName}(...)",
						"typed OsLib CliCommand wrapper or RaiSystem");
				}
				else if (typeName == "RaiSystem" && args.Count >= 1)
				{
					var wrapper = ResolveTypedCliWrapper(args[0].Expression);
					if (wrapper != null)
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitUntypedProcessExecution,
							creation.GetLocation(),
							$"new RaiSystem({args[0].Expression}, ...)",
							wrapper);
					}
				}
			}

			// RAI015: new PitItem("...") with invalid live ID ('{' or '<' or whitespace)
			if (typeName == "PitItem" && args.Count >= 1 && MatchesRule("RAI015"))
			{
				if (args[0].Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
				{
					var idVal = lit.Token.ValueText;
					if (string.IsNullOrWhiteSpace(idVal) || idVal.Contains("{") || idVal.Contains("<"))
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitInvalidPitIdAndUnsafeJsonParse,
							lit.GetLocation(),
							$"Invalid live PitItem ID '{idVal}' violates CR049 (PitItem.ValidateLiveId rejects empty/whitespace IDs and unexpanded '{{' or '<' template placeholders)");
					}
				}
			}

			// RAI016: new *DiagramBuilder("...") with '_' in baseItemId
			if (DiagramBuilderTypes.Contains(typeName) && args.Count >= 1 && MatchesRule("RAI016"))
			{
				if (args[0].Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
				{
					var baseId = lit.Token.ValueText;
					if (baseId.Contains("_"))
					{
						AddFinding(
							DiagnosticDescriptors.EnforceRaiUtilsAndDiagramConventions,
							lit.GetLocation(),
							$"DiagramBuilder baseItemId '{baseId}' must be alphanumeric without '_' or pre-appended archetype suffixes (CR025); the builder appends the archetype suffix automatically");
					}
				}
			}
		}

		// 3. Standalone '+ Os.DIR +' concatenations outside OsLib constructors (RAI005)
		if (MatchesRule("RAI005"))
		{
			foreach (var bin in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
			{
				if (!bin.IsKind(SyntaxKind.AddExpression))
				{
					continue;
				}
				if (bin.Parent is BinaryExpressionSyntax parentBin && parentBin.IsKind(SyntaxKind.AddExpression))
				{
					continue;
				}
				if (bin.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(c => OsLibFileTypes.Contains(GetSimpleTypeName(c.Type))))
				{
					continue;
				}

				if (ContainsExplicitOsDirConcatenation(bin))
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitPathStringConcatenation,
						bin.GetLocation(),
						bin.ToString(),
						"RaiFile");
				}
			}
		}

		// 4. Invocations (RAI006, RAI007, RAI009, RAI012, RAI013, RAI015)
		foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (inv.Expression is not MemberAccessExpressionSyntax ma)
			{
				continue;
			}

			var methodName = ma.Name.Identifier.Text;
			var args = inv.ArgumentList.Arguments;

			// RAI006: Process.Start(...)
			if (MatchesRule("RAI006") && methodName == "Start" && IsProcessTypeAccess(ma.Expression))
			{
				AddFinding(
					DiagnosticDescriptors.ProhibitUntypedProcessExecution,
					ma.Name.GetLocation(),
					"Process.Start",
					"typed OsLib CliCommand wrapper or RaiSystem");
			}

			// RAI007: Environment.SetEnvironmentVariable / GetEnvironmentVariable / GetFolderPath
			if (MatchesRule("RAI007") && IsEnvironmentTypeAccess(ma.Expression))
			{
				if (methodName is "SetEnvironmentVariable" or "GetEnvironmentVariable" or "GetFolderPath")
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitEnvironmentConfigBypass,
						ma.Name.GetLocation(),
						methodName);
				}
			}

			// RAI009: Suspected inverted cp / mv receiver order (source.cp(dest))
			if (MatchesRule("RAI009") && (methodName is "cp" or "mv") && args.Count >= 1)
			{
				var receiverId = ExtractLastIdentifier(ma.Expression);
				var argId = ExtractLastIdentifier(args[0].Expression);
				if (IsClearlySourceIdentifier(receiverId) && IsClearlyDestinationIdentifier(argId))
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitInvertedCopyMoveReceiver,
						ma.Name.GetLocation(),
						ma.Expression.ToString(),
						methodName,
						args[0].Expression.ToString());
				}
			}

			// RAI012: Protected PitItem attributes in ExtendWith / SetProperty / DeleteProperty
			if (MatchesRule("RAI012"))
			{
				if (methodName == "ExtendWith" && args.Count >= 1)
				{
					foreach (var init in args[0].Expression.DescendantNodesAndSelf().OfType<InitializerExpressionSyntax>())
					{
						foreach (var expr in init.Expressions)
						{
							var key = ExtractInitializerPropertyKey(expr);
							if (key is "Id" or "Modified" or "Deleted")
							{
								AddFinding(
									DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
									expr.GetLocation(),
									key);
							}
						}
					}
				}
				else if (methodName is "SetProperty" or "DeleteProperty" or "DeletePropertyPath" && args.Count >= 1)
				{
					var key = ExtractProtectedAttributeArgument(args[0].Expression);
					if (key is "Id" or "Modified" or "Deleted")
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
							args[0].GetLocation(),
							key);
					}
				}
			}

			// RAI013: pit.PitDir.Exists() side effect and pit.Reload() hazard
			if (MatchesRule("RAI013"))
			{
				if (methodName == "Exists" &&
					args.Count == 0 &&
					ma.Expression is MemberAccessExpressionSyntax innerMa &&
					innerMa.Name.Identifier.Text == "PitDir")
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitPitDirExistsAndReloadHazards,
						innerMa.Name.GetLocation(),
						$"'{ma}' triggers a disk 'mkdir()' side effect via the 'PitDir' getter; use '{innerMa.Expression}.JsonFile.Path.Exists()' or '{innerMa.Expression}.JsonFile.Exists()' instead");
				}
				else if (methodName == "Reload" && args.Count == 0 && IsLikelyPitReceiver(ma.Expression))
				{
					AddFinding(
						DiagnosticDescriptors.ProhibitPitDirExistsAndReloadHazards,
						ma.Name.GetLocation(),
						$"'{ma}()' invokes 'Save()' on dirty/merge branches and can overwrite external CLI seeds on disk; use '{ma.Expression}.Load()' for pure disk-to-memory reload");
				}
			}

			// RAI015: JObject.Parse / JArray.Parse / JToken.Parse without DateParseHandling.None
			if (MatchesRule("RAI015") && methodName == "Parse" && IsNewtonsoftJTokenTypeAccess(ma.Expression))
			{
				AddFinding(
					DiagnosticDescriptors.ProhibitInvalidPitIdAndUnsafeJsonParse,
					ma.GetLocation(),
					$"'{ma}(...)' uses default 'DateParseHandling.DateTime' which corrupts ISO-8601 strings in local timezones; parse with 'JsonTextReader {{ DateParseHandling = DateParseHandling.None }}' or 'PitJson' / 'RaidJson5'");
			}
		}

		// 5. Direct Assignment to Protected PitItem Attributes (RAI012)
		if (MatchesRule("RAI012"))
		{
			foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
			{
				if (assign.Parent is InitializerExpressionSyntax)
				{
					continue;
				}

				if (assign.Left is MemberAccessExpressionSyntax propAccess)
				{
					var propName = propAccess.Name.Identifier.Text;
					if (propName is "Modified" or "Deleted" && IsLikelyPitItemReceiver(propAccess.Expression))
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
							propAccess.Name.GetLocation(),
							propName);
					}
				}
				else if (assign.Left is ElementAccessExpressionSyntax elemAccess &&
					elemAccess.ArgumentList.Arguments.Count == 1 &&
					elemAccess.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax keyLit &&
					keyLit.IsKind(SyntaxKind.StringLiteralExpression))
				{
					var key = keyLit.Token.ValueText;
					if (key is "Modified" or "Deleted" && IsLikelyPitItemReceiver(elemAccess.Expression))
					{
						AddFinding(
							DiagnosticDescriptors.ProhibitProtectedPitItemMutation,
							keyLit.GetLocation(),
							key);
					}
				}
			}
		}

		// 6. Member Access Conventions (RAI016: RaiImage.WordCase, RaiImage.StringHelper, ImageNamingConvention.Legacy)
		if (MatchesRule("RAI016"))
		{
			foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
			{
				var text = ma.ToString();
				if (text is "RaiImage.WordCase" or "RaiImage.StringHelper")
				{
					var member = ma.Name.Identifier.Text;
					AddFinding(
						DiagnosticDescriptors.EnforceRaiUtilsAndDiagramConventions,
						ma.GetLocation(),
						$"'{text}' is obsolete (CR019); use 'RaiUtils.{member}' from the 'RaiUtils' namespace instead");
				}
				else if (text == "ImageNamingConvention.Legacy")
				{
					AddFinding(
						DiagnosticDescriptors.EnforceRaiUtilsAndDiagramConventions,
						ma.GetLocation(),
						"'ImageNamingConvention.Legacy' is deprecated for new multi-image and diagram assets; prefer 'ImageNamingConvention.Structured'");
				}
			}

			foreach (var qn in root.DescendantNodes().OfType<QualifiedNameSyntax>())
			{
				var text = qn.ToString();
				if (text is "RaiImage.WordCase" or "RaiImage.StringHelper")
				{
					var member = qn.Right.Identifier.Text;
					AddFinding(
						DiagnosticDescriptors.EnforceRaiUtilsAndDiagramConventions,
						qn.GetLocation(),
						$"'{text}' is obsolete (CR019); use 'RaiUtils.{member}' from the 'RaiUtils' namespace instead");
				}
			}
		}

		// 7. Block/Method Dataflow Rules: RAI008 (Cloud In-Place), RAI011 (Pit RMW), RAI014 (Pit Disposal)
		AnalyzeMethodAndClassSemantics(root, MatchesRule, AddFinding);

		// 8. Formatting Rules (RAI010) when running via CLI syntax engine
		if (includeSystemIoAndFormatting && MatchesRule("RAI010"))
		{
			AnalyzeWhitespaceSyntax(tree, root, AddFinding);
		}

		return findings;
	}

	private static void AnalyzeMethodAndClassSemantics(
		SyntaxNode root,
		Func<string, bool> matchesRule,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		// Method-level checks: RAI008, RAI011, RAI014 (local Pit variables)
		foreach (var methodBody in root.DescendantNodes().OfType<BlockSyntax>())
		{
			// Only analyze outermost method/accessor/local-function blocks for whole-method dataflow
			bool isTopLevelBody = methodBody.Parent is BaseMethodDeclarationSyntax
				or LocalFunctionStatementSyntax
				or AnonymousFunctionExpressionSyntax
				or AccessorDeclarationSyntax;

			// RAI008: Statement order in each block (rm() followed by Save()/cp() on same variable, or dest.mv(tmpFile))
			if (matchesRule("RAI008"))
			{
				AnalyzeBlockCloudInPlace(methodBody, addFinding);
			}

			if (!isTopLevelBody)
			{
				continue;
			}

			// RAI011: Full-snapshot Read-Modify-Write on Pit
			if (matchesRule("RAI011"))
			{
				AnalyzeMethodPitReadModifyWrite(methodBody, addFinding);
			}

			// RAI014 (Part 1): Local new Pit(...) without using or Dispose()
			if (matchesRule("RAI014"))
			{
				AnalyzeMethodLocalPitDisposal(methodBody, addFinding);
			}
		}

		// RAI014 (Part 2): Class fields holding Pit instances not disposed in IDisposable.Dispose()
		if (matchesRule("RAI014"))
		{
			foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			{
				AnalyzeClassPitFieldDisposal(classDecl, addFinding);
			}
		}
	}

	private static void AnalyzeBlockCloudInPlace(
		BlockSyntax block,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		var rmCalledVars = new HashSet<string>(StringComparer.Ordinal);
		var tmpVars = new HashSet<string>(StringComparer.Ordinal);

		foreach (var decl in block.DescendantNodes().OfType<VariableDeclaratorSyntax>())
		{
			if (decl.Initializer?.Value is ObjectCreationExpressionSyntax oc &&
				GetSimpleTypeName(oc.Type) == "TmpFile")
			{
				tmpVars.Add(decl.Identifier.Text);
			}
		}

		foreach (var stmt in block.Statements)
		{
			// If variable is reassigned, clear from rmCalledVars
			foreach (var assign in stmt.DescendantNodes().OfType<AssignmentExpressionSyntax>())
			{
				if (assign.Left is IdentifierNameSyntax id)
				{
					rmCalledVars.Remove(id.Identifier.Text);
				}
			}

			foreach (var inv in stmt.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				if (inv.Expression is not MemberAccessExpressionSyntax ma)
				{
					continue;
				}

				var method = ma.Name.Identifier.Text;
				if (ma.Expression is IdentifierNameSyntax targetId)
				{
					var varName = targetId.Identifier.Text;
					if (method is "Save" or "cp" && rmCalledVars.Contains(varName))
					{
						addFinding(
							DiagnosticDescriptors.EnforceCloudInPlaceInvariant,
							ma.Name.GetLocation(),
							new object[]
							{
								$"Calling '{varName}.rm()' before '{varName}.{method}()' destroys cloud in-place file identity (CR022); truncate and overwrite in-place via '{varName}.DeleteAll().Append(...).Save()' or '{varName}.cp(source)'"
							});
					}
					else if (method == "rm" && inv.ArgumentList.Arguments.Count == 0)
					{
						rmCalledVars.Add(varName);
					}
				}

				if (method == "mv" && inv.ArgumentList.Arguments.Count >= 1)
				{
					var srcExpr = inv.ArgumentList.Arguments[0].Expression;
					var srcText = srcExpr.ToString();
					bool isTmpSource =
						(srcExpr is IdentifierNameSyntax srcId && (tmpVars.Contains(srcId.Identifier.Text) || IsTempIdentifier(srcId.Identifier.Text))) ||
						srcText.Contains("Os.TempDir") ||
						srcText.Contains("new TmpFile");

					if (isTmpSource)
					{
						addFinding(
							DiagnosticDescriptors.EnforceCloudInPlaceInvariant,
							ma.Name.GetLocation(),
							new object[]
							{
								$"Moving temporary file '{srcText}' via '{ma.Expression}.mv({srcText})' violates the Cloud-Storage In-Place Invariant (CR022) and throws RaiCloudStorageException on cloud paths; use '{ma.Expression}.cp({srcText}); {srcText}.rm();' instead"
							});
					}
				}
			}
		}
	}

	private static void AnalyzeMethodPitReadModifyWrite(
		BlockSyntax methodBody,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		var freshPitItems = new HashSet<string>(StringComparer.Ordinal);
		var projectedPitItems = new HashSet<string>(StringComparer.Ordinal);
		var parameterPitItems = new HashSet<string>(StringComparer.Ordinal);
		var extendedVariables = new HashSet<string>(StringComparer.Ordinal);

		if (methodBody.Parent is BaseMethodDeclarationSyntax baseMethod)
		{
			foreach (var param in baseMethod.ParameterList.Parameters)
			{
				if (param.Type != null && GetSimpleTypeName(param.Type) == "PitItem")
				{
					parameterPitItems.Add(param.Identifier.Text);
				}
			}
		}

		foreach (var decl in methodBody.DescendantNodes().OfType<VariableDeclaratorSyntax>())
		{
			var varName = decl.Identifier.Text;
			var initExpr = decl.Initializer?.Value;
			if (initExpr is null)
			{
				continue;
			}

			var declaredType = (decl.Parent as VariableDeclarationSyntax)?.Type;
			if (IsFreshPitItemCreation(initExpr, declaredType) ||
				(initExpr is IdentifierNameSyntax srcFresh && freshPitItems.Contains(srcFresh.Identifier.Text)))
			{
				freshPitItems.Add(varName);
			}
			else if (IsProjectedPitLookup(initExpr) ||
				(initExpr is IdentifierNameSyntax srcProj && projectedPitItems.Contains(srcProj.Identifier.Text)))
			{
				projectedPitItems.Add(varName);
			}
		}

		foreach (var assign in methodBody.DescendantNodes().OfType<AssignmentExpressionSyntax>())
		{
			if (assign.Left is IdentifierNameSyntax id)
			{
				var varName = id.Identifier.Text;
				if (IsFreshPitItemCreation(assign.Right, declaredType: null) ||
					(assign.Right is IdentifierNameSyntax srcFresh && freshPitItems.Contains(srcFresh.Identifier.Text)))
				{
					freshPitItems.Add(varName);
					projectedPitItems.Remove(varName);
				}
				else if (IsProjectedPitLookup(assign.Right) ||
					(assign.Right is IdentifierNameSyntax srcProj && projectedPitItems.Contains(srcProj.Identifier.Text)))
				{
					projectedPitItems.Add(varName);
					freshPitItems.Remove(varName);
				}
			}
		}

		foreach (var inv in methodBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (inv.Expression is MemberAccessExpressionSyntax ma &&
				ma.Name.Identifier.Text == "ExtendWith" &&
				ma.Expression is IdentifierNameSyntax targetId)
			{
				var varName = targetId.Identifier.Text;
				if (!freshPitItems.Contains(varName))
				{
					extendedVariables.Add(varName);
				}
			}
		}

		foreach (var inv in methodBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (inv.Expression is not MemberAccessExpressionSyntax ma)
			{
				continue;
			}

			var method = ma.Name.Identifier.Text;
			if (method == "Add" && inv.ArgumentList.Arguments.Count == 1 && IsLikelyPitReceiver(ma.Expression))
			{
				var argExpr = inv.ArgumentList.Arguments[0].Expression;
				if (argExpr is IdentifierNameSyntax argId)
				{
					var varName = argId.Identifier.Text;
					if (!freshPitItems.Contains(varName) &&
						(projectedPitItems.Contains(varName) ||
							(parameterPitItems.Contains(varName) && extendedVariables.Contains(varName))))
					{
						addFinding(
							DiagnosticDescriptors.ProhibitPitReadModifyWrite,
							inv.GetLocation(),
							new object[] { varName });
					}
				}
				else if (IsProjectedPitLookup(argExpr))
				{
					addFinding(
						DiagnosticDescriptors.ProhibitPitReadModifyWrite,
						inv.GetLocation(),
						new object[] { argExpr.ToString() });
				}
			}
		}
	}

	private static void AnalyzeMethodLocalPitDisposal(
		BlockSyntax methodBody,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		foreach (var localDecl in methodBody.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
		{
			if (!localDecl.UsingKeyword.IsKind(SyntaxKind.None))
			{
				continue;
			}

			foreach (var declarator in localDecl.Declaration.Variables)
			{
				if (declarator.Initializer?.Value is not ObjectCreationExpressionSyntax oc)
				{
					continue;
				}

				if (GetSimpleTypeName(oc.Type) != "Pit")
				{
					continue;
				}

				// If unflagged: true is explicitly passed, skip
				if (oc.ArgumentList != null && oc.ArgumentList.Arguments.Any(a =>
					a.NameColon?.Name.Identifier.Text == "unflagged" &&
					a.Expression.ToString() == "true"))
				{
					continue;
				}

				var varName = declarator.Identifier.Text;

				// Check if disposed, returned, or stored in another structure within the method
				bool isDisposedOrEscapes = methodBody.DescendantNodes()
					.OfType<InvocationExpressionSyntax>()
					.Any(inv => inv.Expression is MemberAccessExpressionSyntax ma &&
						ma.Name.Identifier.Text == "Dispose" &&
						ma.Expression is IdentifierNameSyntax id &&
						id.Identifier.Text == varName)
					|| methodBody.DescendantNodes()
						.OfType<ConditionalAccessExpressionSyntax>()
						.Any(ca => ca.Expression is IdentifierNameSyntax id &&
							id.Identifier.Text == varName &&
							ca.WhenNotNull.ToString().Contains("Dispose"))
					|| methodBody.DescendantNodes()
						.OfType<ReturnStatementSyntax>()
						.Any(ret => ret.Expression != null && ret.Expression.DescendantNodesAndSelf()
							.OfType<IdentifierNameSyntax>()
							.Any(id => id.Identifier.Text == varName));

				if (!isDisposedOrEscapes)
				{
					addFinding(
						DiagnosticDescriptors.EnforcePitInstanceDisposal,
						declarator.Identifier.GetLocation(),
						new object[] { varName });
				}
			}
		}
	}

	private static void AnalyzeClassPitFieldDisposal(
		ClassDeclarationSyntax classDecl,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		var disposeMethods = classDecl.Members
			.OfType<MethodDeclarationSyntax>()
			.Where(m => m.Identifier.Text is "Dispose" or "DisposeAsync")
			.ToList();

		if (disposeMethods.Count == 0)
		{
			return;
		}

		var pitFields = new List<(string Name, Location Location)>();

		foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
		{
			var fieldTypeName = GetSimpleTypeName(field.Declaration.Type);
			if (fieldTypeName == "Pit")
			{
				foreach (var v in field.Declaration.Variables)
				{
					pitFields.Add((v.Identifier.Text, v.Identifier.GetLocation()));
				}
			}
		}

		foreach (var (fieldName, fieldLocation) in pitFields)
		{
			bool disposedInDispose = disposeMethods.Any(dm =>
				dm.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(inv =>
					inv.Expression is MemberAccessExpressionSyntax ma &&
					ma.Name.Identifier.Text is "Dispose" or "DisposeAsync" &&
					ExtractLastIdentifier(ma.Expression) == fieldName) ||
				dm.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>().Any(ca =>
					ExtractLastIdentifier(ca.Expression) == fieldName &&
					ca.WhenNotNull.ToString().Contains("Dispose")));

			if (!disposedInDispose)
			{
				addFinding(
					DiagnosticDescriptors.EnforcePitInstanceDisposal,
					fieldLocation,
					new object[] { fieldName });
			}
		}
	}

	private static void AnalyzeSystemIoSyntax(
		SyntaxNode root,
		Func<string, bool> matchesRule,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
	{
		// 1. Invocations: Directory.*, File.*, ZipFile.*, Path.*
		foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (inv.Expression is not MemberAccessExpressionSyntax memberAccess)
			{
				continue;
			}

			var methodName = memberAccess.Name.Identifier.Text;
			if (IsDirectoryTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoDirectory,
					memberAccess.Name.GetLocation(),
					new object[] { methodName });
			}
			else if (IsFileTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoFile,
					memberAccess.Name.GetLocation(),
					new object[] { methodName });
			}
			else if (IsPathTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoPath,
					memberAccess.Name.GetLocation(),
					new object[] { methodName });
			}
		}

		// 2. Method Group References & Static Fields (e.g. .Select(Path.GetFileName), Path.DirectorySeparatorChar)
		foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
		{
			if (memberAccess.Parent is InvocationExpressionSyntax inv && inv.Expression == memberAccess)
			{
				continue;
			}
			if (memberAccess.Parent is MemberAccessExpressionSyntax parentMa && parentMa.Expression == memberAccess)
			{
				continue;
			}

			var memberName = memberAccess.Name.Identifier.Text;
			if (IsDirectoryTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoDirectory,
					memberAccess.Name.GetLocation(),
					new object[] { memberName });
			}
			else if (IsFileTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoFile,
					memberAccess.Name.GetLocation(),
					new object[] { memberName });
			}
			else if (IsPathTypeAccess(memberAccess.Expression))
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoPath,
					memberAccess.Name.GetLocation(),
					new object[] { memberName });
			}
		}

		// 3. Object Creation: new DirectoryInfo(...), new FileInfo(...), new FileStream(...), new StreamReader(...), new StreamWriter(...)
		foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
		{
			var typeName = GetSimpleTypeName(creation.Type);
			if (typeName == "DirectoryInfo")
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoDirectory,
					creation.Type.GetLocation(),
					new object[] { "DirectoryInfo" });
			}
			else if (typeName is "FileInfo" or "FileStream" or "StreamReader" or "StreamWriter")
			{
				addFinding(
					DiagnosticDescriptors.ProhibitSystemIoFile,
					creation.Type.GetLocation(),
					new object[] { typeName });
			}
		}
	}

	private static void AnalyzeWhitespaceSyntax(
		SyntaxTree tree,
		SyntaxNode root,
		Action<DiagnosticDescriptor, Location, object[]> addFinding)
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
			if (line.Span.Length == 0)
			{
				continue;
			}
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
					int firstSpaceOffset = indent.IndexOf(' ');
					var span = new Microsoft.CodeAnalysis.Text.TextSpan(line.Start + firstSpaceOffset, 1);
					addFinding(
						DiagnosticDescriptors.EnforceWhitespaceFormatting,
						Location.Create(tree, span),
						new object[] { WhitespaceFormattingAnalyzer.TabIndentationMessage });
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
						var span = new Microsoft.CodeAnalysis.Text.TextSpan(tokenA.Span.End + spaceIdx, 2);
						addFinding(
							DiagnosticDescriptors.EnforceWhitespaceFormatting,
							Location.Create(tree, span),
							new object[] { WhitespaceFormattingAnalyzer.ConsecutiveSpacesMessage });
					}
				}
			}
		}
	}

	public static bool IsDirectoryTypeAccess(ExpressionSyntax expression)
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

	public static bool IsFileTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text is "File" or "ZipFile";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var text = ma.ToString();
			return text is "System.IO.File" or "IO.File" or "System.IO.Compression.ZipFile" or "Compression.ZipFile";
		}
		return false;
	}

	public static bool IsPathTypeAccess(ExpressionSyntax expression)
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

	private static bool IsProcessTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text == "Process";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var text = ma.ToString();
			return text is "System.Diagnostics.Process" or "Diagnostics.Process";
		}
		return false;
	}

	private static bool IsEnvironmentTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text == "Environment";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			return ma.ToString() == "System.Environment";
		}
		return false;
	}

	private static bool IsNewtonsoftJTokenTypeAccess(ExpressionSyntax expression)
	{
		if (expression is IdentifierNameSyntax id)
		{
			return id.Identifier.Text is "JObject" or "JArray" or "JToken";
		}
		if (expression is MemberAccessExpressionSyntax ma)
		{
			var name = ma.Name.Identifier.Text;
			return name is "JObject" or "JArray" or "JToken";
		}
		return false;
	}

	private static string GetSimpleTypeName(TypeSyntax type)
	{
		return type switch
		{
			IdentifierNameSyntax id => id.Identifier.Text,
			QualifiedNameSyntax qn => qn.Right.Identifier.Text,
			GenericNameSyntax gn => gn.Identifier.Text,
			NullableTypeSyntax nt => GetSimpleTypeName(nt.ElementType),
			_ => type.ToString()
		};
	}

	private static bool ContainsPathConcatenation(ExpressionSyntax expr)
	{
		foreach (var bin in expr.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>())
		{
			if (!bin.IsKind(SyntaxKind.AddExpression))
			{
				continue;
			}

			if (ContainsExplicitOsDirConcatenation(bin))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsExplicitOsDirConcatenation(BinaryExpressionSyntax bin)
	{
		var text = bin.ToString();
		if (text.Contains("Os.DIR"))
		{
			return true;
		}

		// Also flag <path>.ToString() + "/" + ... or <path>.Path + "/" + ...
		bool hasToStringOrPath = bin.DescendantNodesAndSelf()
			.OfType<MemberAccessExpressionSyntax>()
			.Any(m => m.Name.Identifier.Text is "ToString" or "Path");

		bool hasSlashLiteral = bin.DescendantNodesAndSelf()
			.OfType<LiteralExpressionSyntax>()
			.Any(l => l.IsKind(SyntaxKind.StringLiteralExpression) &&
				(l.Token.ValueText.StartsWith("/", StringComparison.Ordinal) ||
				l.Token.ValueText.EndsWith("/", StringComparison.Ordinal)));

		return hasToStringOrPath && hasSlashLiteral;
	}

	private static string? ResolveTypedCliWrapper(ExpressionSyntax exeExpr)
	{
		if (exeExpr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
		{
			var raw = lit.Token.ValueText.Trim();
			var lastSegment = raw.Split('/', '\\').Last();
			if (lastSegment.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				lastSegment = lastSegment.Substring(0, lastSegment.Length - 4);
			}
			if (TypedCliWrappers.TryGetValue(lastSegment, out var wrapper))
			{
				return wrapper;
			}
		}
		else if (exeExpr is IdentifierNameSyntax id)
		{
			var name = id.Identifier.Text;
			if (name.IndexOf("Pits", StringComparison.OrdinalIgnoreCase) >= 0) return "PitsCommand";
			if (name.IndexOf("Iorg", StringComparison.OrdinalIgnoreCase) >= 0) return "IorgCommand";
			if (name.IndexOf("Raid", StringComparison.OrdinalIgnoreCase) >= 0) return "RaidCommand";
			if (name.IndexOf("Amafu", StringComparison.OrdinalIgnoreCase) >= 0) return "AmafuCommand";
		}
		return null;
	}

	private static string ExtractLastIdentifier(ExpressionSyntax expr)
	{
		return expr switch
		{
			IdentifierNameSyntax id => id.Identifier.Text,
			MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
			_ => expr.ToString()
		};
	}

	private static bool IsClearlySourceIdentifier(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		return name.Equals("src", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("source", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("from", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("src", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("source", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("fromFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("fromPath", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("inFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("inputFile", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsClearlyDestinationIdentifier(string name)
	{
		if (string.IsNullOrEmpty(name)) return false;
		return name.Equals("dst", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("dest", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("destination", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("to", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("dst", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("dest", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("toFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("toPath", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("outFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("outputFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("targetFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("backupTarget", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTempIdentifier(string name)
	{
		return name.Equals("tmp", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("tmpFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("tempFile", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("workingOutput", StringComparison.OrdinalIgnoreCase);
	}

	private static string? ExtractInitializerPropertyKey(ExpressionSyntax expr)
	{
		if (expr is AssignmentExpressionSyntax assign)
		{
			if (assign.Left is ImplicitElementAccessSyntax implicitElem &&
				implicitElem.ArgumentList.Arguments.Count == 1 &&
				implicitElem.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit1 &&
				lit1.IsKind(SyntaxKind.StringLiteralExpression))
			{
				return lit1.Token.ValueText;
			}

			if (assign.Left is ElementAccessExpressionSyntax elem &&
				elem.ArgumentList.Arguments.Count == 1 &&
				elem.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit2 &&
				lit2.IsKind(SyntaxKind.StringLiteralExpression))
			{
				return lit2.Token.ValueText;
			}

			if (assign.Left is IdentifierNameSyntax id)
			{
				return id.Identifier.Text;
			}
		}
		return null;
	}

	private static string? ExtractProtectedAttributeArgument(ExpressionSyntax argExpr)
	{
		if (argExpr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
		{
			return lit.Token.ValueText;
		}
		if (argExpr is ObjectCreationExpressionSyntax oc &&
			GetSimpleTypeName(oc.Type) == "JProperty" &&
			oc.ArgumentList?.Arguments.Count >= 1 &&
			oc.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax propLit &&
			propLit.IsKind(SyntaxKind.StringLiteralExpression))
		{
			return propLit.Token.ValueText;
		}
		return null;
	}

	private static bool IsLikelyPitReceiver(ExpressionSyntax expr)
	{
		var name = ExtractLastIdentifier(expr);
		return name.Equals("pit", StringComparison.OrdinalIgnoreCase) ||
			name.EndsWith("Pit", StringComparison.Ordinal) ||
			name is "Person" or "Object" or "Place" or "Activity";
	}

	private static bool IsLikelyPitItemReceiver(ExpressionSyntax expr)
	{
		if (expr is ElementAccessExpressionSyntax elem && IsLikelyPitReceiver(elem.Expression))
		{
			return true;
		}
		var name = ExtractLastIdentifier(expr);
		return name.Equals("item", StringComparison.OrdinalIgnoreCase) ||
			name.EndsWith("Item", StringComparison.Ordinal) ||
			name.Equals("delta", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("existing", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("entry", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsFreshPitItemCreation(ExpressionSyntax expr, TypeSyntax? declaredType)
	{
		if (expr is ObjectCreationExpressionSyntax oc && GetSimpleTypeName(oc.Type) == "PitItem")
		{
			return true;
		}

		if (expr is ImplicitObjectCreationExpressionSyntax &&
			declaredType != null &&
			GetSimpleTypeName(declaredType) == "PitItem")
		{
			return true;
		}

		if (expr is InvocationExpressionSyntax inv &&
			inv.Expression is MemberAccessExpressionSyntax ma &&
			ma.Name.Identifier.Text == "Item" &&
			ExtractLastIdentifier(ma.Expression) == "SparsePayload")
		{
			return true;
		}

		return false;
	}

	private static bool IsProjectedPitLookup(ExpressionSyntax expr)
	{
		if (expr is PostfixUnaryExpressionSyntax postfix &&
			postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
		{
			return IsProjectedPitLookup(postfix.Operand);
		}

		if (expr is ParenthesizedExpressionSyntax paren)
		{
			return IsProjectedPitLookup(paren.Expression);
		}

		if (expr is BinaryExpressionSyntax bin &&
			bin.IsKind(SyntaxKind.CoalesceExpression))
		{
			return IsProjectedPitLookup(bin.Left) || IsProjectedPitLookup(bin.Right);
		}

		if (expr is ConditionalExpressionSyntax cond)
		{
			return IsProjectedPitLookup(cond.WhenTrue) || IsProjectedPitLookup(cond.WhenFalse);
		}

		if (expr is InvocationExpressionSyntax inv &&
			inv.Expression is MemberAccessExpressionSyntax ma &&
			ma.Name.Identifier.Text is "Get" or "GetAt" &&
			IsLikelyPitReceiver(ma.Expression))
		{
			return true;
		}

		if (expr is ElementAccessExpressionSyntax elem &&
			IsLikelyPitReceiver(elem.Expression))
		{
			return true;
		}

		return false;
	}
}
