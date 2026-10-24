using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace RaiGuard.Core.Diagnostics;

public sealed class RuleGroup
{
	public string Name { get; }
	public string Description { get; }
	public ImmutableArray<string> RuleIds { get; }

	public RuleGroup(string name, string description, params string[] ruleIds)
	{
		Name = name;
		Description = description;
		RuleIds = ImmutableArray.Create(ruleIds);
	}

	public bool Contains(string ruleId) =>
		RuleIds.Any(id => string.Equals(id, ruleId, StringComparison.OrdinalIgnoreCase));
}

public sealed class RuleCatalogEntry
{
	public string Id { get; }
	public DiagnosticDescriptor Descriptor { get; }
	public string Remediation { get; }
	public ImmutableArray<string> Groups { get; }

	public RuleCatalogEntry(string id, DiagnosticDescriptor descriptor, string remediation, params string[] groups)
	{
		Id = id;
		Descriptor = descriptor;
		Remediation = remediation;
		Groups = ImmutableArray.Create(groups);
	}
}

public static class DiagnosticDescriptors
{
	private const string Category = "RaiGuard.Reliability";
	private const string FormattingCategory = "RaiGuard.Formatting";
	private const string JsonPitCategory = "RaiGuard.JsonPit";
	private const string ArchitectureCategory = "RaiGuard.Architecture";

	public static readonly DiagnosticDescriptor ProhibitSystemIoDirectory = new(
		id: "RAI001",
		title: "Prohibit direct usage of System.IO.Directory",
		messageFormat: "'System.IO.Directory.{0}' is prohibited; use 'RaiPath' methods instead (e.g., RaiPath.EnumerateDirectories or RaiPath.EnumerateFiles)",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces framework-specific path physics by requiring RaiPath abstractions instead of System.IO.Directory or System.IO.DirectoryInfo.");

	public static readonly DiagnosticDescriptor ProhibitSystemIoFile = new(
		id: "RAI002",
		title: "Prohibit direct usage of System.IO.File",
		messageFormat: "'System.IO.File.{0}' is prohibited; use 'RaiFile' or 'TextFile' methods instead (e.g., new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces framework-specific path physics by requiring RaiFile and TextFile abstractions instead of System.IO.File, FileInfo, FileStream, StreamReader, StreamWriter, or ZipFile.");

	public static readonly DiagnosticDescriptor ProhibitSystemIoPath = new(
		id: "RAI003",
		title: "Prohibit direct usage of System.IO.Path",
		messageFormat: "'System.IO.Path.{0}' is prohibited; use 'RaiPath' or 'RaiRelPath' with operator '/' instead",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces framework-specific path physics by requiring RaiPath abstractions and operator '/' instead of System.IO.Path.");

	public static readonly DiagnosticDescriptor ProhibitRaiPathWithoutTrailingSlash = new(
		id: "RAI004",
		title: "Directory string literal passed to new RaiPath must end with '/'",
		messageFormat: "'new RaiPath(\"{0}\")' lacks a trailing '/'; RaiPath(string) strips non-slash-terminated trailing segments as filenames — use 'new RaiPath(\"{0}/\")' or operator '/'",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "In OsLib, new RaiPath(string) splits the path and treats any non-slash-terminated final segment as a file name. Directory literals must end with '/' or use the '/' operator.");

	public static readonly DiagnosticDescriptor ProhibitPathStringConcatenation = new(
		id: "RAI005",
		title: "Prohibit string concatenation with Os.DIR or '/' when building paths or files",
		messageFormat: "String concatenation '{0}' bypasses OsLib path physics and risks double slashes; use 'RaiPath' operator '/' for directories or structured 'new {1}(dir, name, ext)' constructors for files",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "RaiPath.ToString() already ends with Os.DIR ('/'). Concatenating '+ Os.DIR +' or '+ \"/\" +' creates redundant separators and bypasses structured OsLib constructors.");

	public static readonly DiagnosticDescriptor ProhibitUntypedProcessExecution = new(
		id: "RAI006",
		title: "Use typed OsLib CliCommand wrappers instead of raw Process or untyped RaiSystem",
		messageFormat: "Direct process execution '{0}' bypasses concurrency gates and argument tokenization; use '{1}' instead",
		category: ArchitectureCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces usage of typed OsLib CliCommand subclasses (PitsCommand, IorgCommand, RaidCommand, AmafuCommand, JpitCommand, DenoCommand, RCloneCommand, UnzipCommand, ImageMagickCommand, PlantUmlCommand) instead of raw Process.Start or untyped RaiSystem.");

	public static readonly DiagnosticDescriptor ProhibitEnvironmentConfigBypass = new(
		id: "RAI007",
		title: "Enforce sacred Os.Config runtime configuration over Environment APIs",
		messageFormat: "'Environment.{0}' bypasses immutable 'Os.Config' runtime configuration; use 'Os.Config', 'Os.TempDir', 'Os.LocalBackupDir', 'Os.UserHomeDir', or 'WebApplicationFactory.UseSetting' instead",
		category: ArchitectureCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Os.Config (~/.config/RAIkeep.json5) is loaded once per process as the single source of truth for cloud roots and runtime paths. Direct Environment variable mutation or folder resolution is prohibited.");

	public static readonly DiagnosticDescriptor EnforceCloudInPlaceInvariant = new(
		id: "RAI008",
		title: "Enforce Cloud-Storage In-Place Invariant (no temp-to-cloud mv or rm-before-Save)",
		messageFormat: "{0}",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces CR022 Cloud-Storage In-Place Invariant: never unlink a file via rm() before Save()/cp(), and never move a temporary file into a cloud destination via mv() instead of in-place cp().");

	public static readonly DiagnosticDescriptor ProhibitInvertedCopyMoveReceiver = new(
		id: "RAI009",
		title: "Prohibit inverted OsLib cp/mv receiver order (destination must be receiver)",
		messageFormat: "Suspected inverted receiver in '{0}.{1}({2})': in OsLib, the destination is the receiver ('this') and the source is the argument ('{2}.{1}({0})')",
		category: Category,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "In OsLib (RaiFile.cp/mv and RaiPath.cp/mv), the destination object is the receiver and the source object is passed as the first parameter: destination.cp(source).");

	public static readonly DiagnosticDescriptor EnforceWhitespaceFormatting = new(
		id: "RAI010",
		title: "Enforce tab indentation and prohibit consecutive spaces",
		messageFormat: "{0}",
		category: FormattingCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces codebase formatting invariants: all indentation must use tabs ('\\t'), and tokens outside string/char literals must not have consecutive spaces.");

	public static readonly DiagnosticDescriptor ProhibitPitReadModifyWrite = new(
		id: "RAI011",
		title: "Prohibit Pit full-snapshot Read-Modify-Write (ExtendWith on projected item)",
		messageFormat: "Full-snapshot Read-Modify-Write on '{0}' stomps concurrent cloud replica fields and risks ProtectedAttributeException; create a sparse delta ('var delta = new PitItem(id); delta.ExtendWith(patch); pit.Add(delta);') or mutate live properties directly",
		category: JsonPitCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces CR040 No-Read-Modify-Write Law: never pass a projected PitItem from pit.Get(id) or pit[id] through ExtendWith() back into pit.Add(). Emit minimal sparse PitItem deltas instead.");

	public static readonly DiagnosticDescriptor ProhibitProtectedPitItemMutation = new(
		id: "RAI012",
		title: "Prohibit direct mutation or payload injection of protected PitItem attributes (Id, Modified, Deleted)",
		messageFormat: "Protected PitItem lifecycle attribute '{0}' must not be mutated directly or injected via 'ExtendWith'; use 'new PitItem(id)', 'pit.Add(item)', 'pit.AddHistorical(item)', or 'pit.Delete(id)'",
		category: JsonPitCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Id, Modified, and Deleted are protected lifecycle attributes managed exclusively by JsonPit. Mutating them directly or injecting them in client payloads throws ProtectedAttributeException or TombstoneException.");

	public static readonly DiagnosticDescriptor ProhibitPitDirExistsAndReloadHazards = new(
		id: "RAI013",
		title: "Prohibit pit.PitDir.Exists() mkdir side-effect and pit.Reload() overwrite hazard",
		messageFormat: "{0}",
		category: JsonPitCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Pit.PitDir property getter executes mkdir() on disk as a side effect (use pit.JsonFile.Path.Exists() instead), and Pit.Reload() calls Save() which can overwrite external CLI seeds (use pit.Load() for pure disk reload).");

	public static readonly DiagnosticDescriptor EnforcePitInstanceDisposal = new(
		id: "RAI014",
		title: "Enforce Pit instance disposal to release canonical path reservation and process flag",
		messageFormat: "Pit instance '{0}' is not disposed; use 'using var {0} = new Pit(...)' or call '{0}.Dispose()' in IDisposable.Dispose() to prevent PitInstanceConflictException",
		category: JsonPitCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Pit registers canonical path ownership in process-wide ActiveCanonicalPitPaths and maintains a process activity flag file. Every Pit instance must be deterministically disposed.");

	public static readonly DiagnosticDescriptor ProhibitInvalidPitIdAndUnsafeJsonParse = new(
		id: "RAI015",
		title: "Prohibit template placeholders in live PitItem IDs and unsafe JObject.Parse without DateParseHandling.None",
		messageFormat: "{0}",
		category: JsonPitCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces CR049 live ID validation (no '{' or '<' template placeholders) and prohibits default JObject.Parse / JToken.Parse which corrupt ISO-8601 strings via DateParseHandling.DateTime.");

	public static readonly DiagnosticDescriptor EnforceRaiUtilsAndDiagramConventions = new(
		id: "RAI016",
		title: "Enforce RaiUtils.WordCase, Structured image naming, and alphanumeric DiagramBuilder BaseItemIds",
		messageFormat: "{0}",
		category: ArchitectureCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Enforces CR019 (use RaiUtils.WordCase instead of obsolete RaiImage.WordCase), CR025 (DiagramBuilder baseItemId must be alphanumeric without pre-appended '_UCD'/'_CD' suffix), and ImageNamingConvention.Structured.");

	public static readonly DiagnosticDescriptor ProhibitAdHocMediaUrlComposition = new(
		id: "RAI017",
		title: "Prohibit ad-hoc '/img/' media URL string composition",
		messageFormat: "Do not compose '/img/' media URLs using string interpolation or concatenation. Use 'MediaUrls.{Video|Poster|Image}(...)' instead.",
		category: ArchitectureCategory,
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Media URLs must be produced by the canonical MediaUrls helper to ensure proper parameterization, zero hardcoded extensions, and encapsulation of storage topology.");

	public static readonly ImmutableArray<RuleGroup> Groups = ImmutableArray.Create(
		new RuleGroup(
			"OsLib",
			"Path algebra (RaiPath/RaiRelPath), file/directory objects (RaiFile/TextFile), typed CliCommand execution, and sacred Os.Config",
			"RAI001", "RAI002", "RAI003", "RAI004", "RAI005", "RAI006", "RAI007", "RAI008", "RAI009"),
		new RuleGroup(
			"CloudSync",
			"Cloud-drive replication invariants (CR022 in-place writes, CR040 sparse deltas, CR033 canonical path reservation, and reload safety)",
			"RAI001", "RAI002", "RAI004", "RAI007", "RAI008", "RAI011", "RAI013", "RAI014"),
		new RuleGroup(
			"JsonPit",
			"Open-world sparse Pit and PitItem persistence, temporal merge, protected lifecycle attributes, PitsCommand gates, and DateParseHandling.None",
			"RAI006", "RAI008", "RAI011", "RAI012", "RAI013", "RAI014", "RAI015"),
		new RuleGroup(
			"CliCommand",
			"Typed OsLib CLI command execution (PitsCommand, IorgCommand, RaidCommand, AmafuCommand) and external CLI seed reload safety",
			"RAI006", "RAI013"),
		new RuleGroup(
			"RaiMedia",
			"RaiUtils.WordCase (CR019), RaiImage (ImageNamingConvention.Structured, IorgCommand), RaiDiagram (DiagramBuilder baseItemId per CR025, RaidCommand), and canonical MediaUrls (RAI017)",
			"RAI006", "RAI008", "RAI016", "RAI017"),
		new RuleGroup(
			"Formatting",
			"Tab indentation ('\\t') and single-space token separation invariants",
			"RAI010")
	);

	public static readonly ImmutableArray<RuleCatalogEntry> Catalog = ImmutableArray.Create(
		new RuleCatalogEntry("RAI001", ProhibitSystemIoDirectory, "Use RaiPath methods (e.g. new RaiPath(path).EnumerateDirectories(), .EnumerateFiles(), .Exists(), .mkdir(), .rmdir())", "OsLib", "CloudSync"),
		new RuleCatalogEntry("RAI002", ProhibitSystemIoFile, "Use RaiFile or TextFile methods (e.g. new RaiFile(path).Exists(), new RaiFile(path).rm(), new TextFile(path).Read(), new TextFile(path, content))", "OsLib", "CloudSync"),
		new RuleCatalogEntry("RAI003", ProhibitSystemIoPath, "Use RaiPath or RaiRelPath with operator '/' (e.g. new RaiPath(a) / b), RaiFile.Name, Os.TempDir, or Os.DIR", "OsLib"),
		new RuleCatalogEntry("RAI004", ProhibitRaiPathWithoutTrailingSlash, "Ensure directory string literals passed to new RaiPath(\"...\") end with '/' or compose segments via operator '/'", "OsLib", "CloudSync"),
		new RuleCatalogEntry("RAI005", ProhibitPathStringConcatenation, "Use 'dir / subdir' for paths and structured 'new TextFile(dir, name, ext)' or 'new RaiFile(dir, name, ext)' constructors for files", "OsLib"),
		new RuleCatalogEntry("RAI006", ProhibitUntypedProcessExecution, "Use typed OsLib CliCommand wrappers (PitsCommand, IorgCommand, RaidCommand, AmafuCommand, JpitCommand, DenoCommand, RCloneCommand, UnzipCommand)", "OsLib", "JsonPit", "CliCommand", "RaiMedia"),
		new RuleCatalogEntry("RAI007", ProhibitEnvironmentConfigBypass, "Read runtime paths from Os.Config, Os.TempDir, Os.LocalBackupDir, Os.UserHomeDir, and use WebApplicationFactory.UseSetting in tests", "OsLib", "CloudSync"),
		new RuleCatalogEntry("RAI008", EnforceCloudInPlaceInvariant, "Truncate/overwrite files in-place via TextFile.DeleteAll().Append(...).Save() or dest.cp(tmp); tmp.rm() instead of rm() or mv(tmp)", "OsLib", "CloudSync", "JsonPit", "RaiMedia"),
		new RuleCatalogEntry("RAI009", ProhibitInvertedCopyMoveReceiver, "Place the destination as receiver and source as argument: destination.cp(source) / destination.mv(source)", "OsLib"),
		new RuleCatalogEntry("RAI010", EnforceWhitespaceFormatting, "Indent code with tabs ('\\t') and use single spaces between tokens", "Formatting"),
		new RuleCatalogEntry("RAI011", ProhibitPitReadModifyWrite, "Create a minimal sparse delta ('var delta = new PitItem(id); delta.ExtendWith(patch); pit.Add(delta);') or mutate live properties directly ('pit[id][\"Prop\"] = val; pit.Save();')", "JsonPit", "CloudSync"),
		new RuleCatalogEntry("RAI012", ProhibitProtectedPitItemMutation, "Do not set or pass Id, Modified, or Deleted in client payloads; use new PitItem(id), pit.Add(item), pit.AddHistorical(item), or pit.Delete(id)", "JsonPit"),
		new RuleCatalogEntry("RAI013", ProhibitPitDirExistsAndReloadHazards, "Use pit.JsonFile.Path.Exists() instead of pit.PitDir.Exists(), and use pit.Load() instead of pit.Reload() for pure disk reloads", "JsonPit", "CloudSync", "CliCommand"),
		new RuleCatalogEntry("RAI014", EnforcePitInstanceDisposal, "Wrap local Pit instances in 'using var pit = new Pit(...)' and call '_pit?.Dispose()' in IDisposable.Dispose()", "JsonPit", "CloudSync"),
		new RuleCatalogEntry("RAI015", ProhibitInvalidPitIdAndUnsafeJsonParse, "Pass concrete validated IDs (without '{' or '<') to PitItem, and parse JSON with DateParseHandling.None (PitJson / RaidJson5)", "JsonPit"),
		new RuleCatalogEntry("RAI016", EnforceRaiUtilsAndDiagramConventions, "Use RaiUtils.WordCase, ImageNamingConvention.Structured, and alphanumeric DiagramBuilder baseItemIds without '_' suffixes", "RaiMedia"),
		new RuleCatalogEntry("RAI017", ProhibitAdHocMediaUrlComposition, "Use canonical 'MediaUrls.Video(...)', 'MediaUrls.Poster(...)', or 'MediaUrls.Image(...)' instead of manual string formatting", "RaiMedia")
	);

	public static HashSet<string>? ResolveFilterSet(string? ruleFilter, string? groupFilter)
	{
		if (string.IsNullOrWhiteSpace(ruleFilter) && string.IsNullOrWhiteSpace(groupFilter))
		{
			return null;
		}

		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!string.IsNullOrWhiteSpace(ruleFilter))
		{
			foreach (var part in ruleFilter!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var trimmed = part.Trim();
				if (trimmed.Length > 0)
				{
					set.Add(trimmed);
				}
			}
		}

		if (!string.IsNullOrWhiteSpace(groupFilter))
		{
			foreach (var part in groupFilter!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var groupName = part.Trim();
				var matchedGroup = Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
				if (matchedGroup != null)
				{
					foreach (var id in matchedGroup.RuleIds)
					{
						set.Add(id);
					}
				}
			}
		}

		return set;
	}
}
