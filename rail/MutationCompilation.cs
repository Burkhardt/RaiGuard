using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace RaiGuard;

/// <summary>
/// One compilation for a rail inspection. Prefer each input project's restored
/// compile assets, then host references. No restore, build or inspected code execution.
/// Missing references remain unresolved; they never justify a mutation warning.
/// </summary>
internal static class MutationCompilation
{
	internal static CSharpCompilation Create(IReadOnlyList<string> files)
	{
		var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		void Add(string path)
		{
			if (File.Exists(path)) paths.TryAdd(Path.GetFileName(path), path);
		}
		var assetsFiles = new HashSet<string>();
		foreach (var file in files)
		{
			for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!); directory != null; directory = directory.Parent)
			{
				var assets = Path.Combine(directory.FullName, "obj", "project.assets.json");
				if (File.Exists(assets)) { assetsFiles.Add(assets); break; }
			}
		}
		foreach (var assetFile in assetsFiles)
		{
			using var document = JsonDocument.Parse(File.ReadAllText(assetFile));
			var root = document.RootElement;
			if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("packageFolders", out var folders) || !root.TryGetProperty("libraries", out var libraries)) continue;
			foreach (var target in targets.EnumerateObject())
			foreach (var library in target.Value.EnumerateObject())
			{
				if (!library.Value.TryGetProperty("compile", out var compile) || !libraries.TryGetProperty(library.Name, out var info) || !info.TryGetProperty("path", out var packagePath)) continue;
				foreach (var item in compile.EnumerateObject())
				foreach (var folder in folders.EnumerateObject())
					if (item.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) Add(Path.Combine(folder.Name, packagePath.GetString()!, item.Name));
			}
		}
		foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator)) Add(path);
		foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")) Add(path);
		Add(typeof(JsonPit.Pit).Assembly.Location);
		Add(typeof(Newtonsoft.Json.Linq.JObject).Assembly.Location);
		var trees = files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToList();
		var references = paths.Values.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("RailInspection", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		if (compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
			Console.Error.WriteLine("rail: inspection compilation has unresolved or invalid code; RAI011/RAI012 report only proven cases. No finding does not establish complete mutation safety. Use the compiler analyzer for project-context validation.");
		return compilation;
	}
}
