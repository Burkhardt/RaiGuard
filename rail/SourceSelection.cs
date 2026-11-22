using OsLib;

namespace RaiGuard;

/// <summary>Read/validation boundary; production always uses the real filesystem.</summary>
internal class SourceFileSystem
{
	internal virtual FileAttributes Attributes(string path) => LocalFileSystem.Attributes(path);
	internal virtual IEnumerable<string> Entries(string directory) => LocalFileSystem.Entries(directory);
	internal virtual void ValidateReadable(string path)
	{
		using var stream = LocalFileSystem.OpenSharedRead(path);
	}
	internal virtual string ReadAllText(string path) => WithPath(path, () => LocalFileSystem.ReadText(path));
	internal virtual void WriteAllText(string path, string content) => WithPath(path, () => { LocalFileSystem.WriteText(path, content); return true; });
	internal static T WithPath<T>(string path, Func<T> operation)
	{
		try { return operation(); }
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			throw new IOException($"Cannot access '{path}': {ex.Message}", ex);
		}
	}
}

internal static class SourceSelection
{
	internal static IReadOnlyList<string> Resolve(IReadOnlyList<string> targets, SourceFileSystem fileSystem)
	{
		// Preserve actual entry spelling rather than assuming all macOS volumes are
		// case-insensitive. Canonicalizing directory entries also collapses aliases
		// in casing on case-insensitive volumes without conflating distinct Unix files.
		var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var names = new Dictionary<string, string>(pathComparer);
		string Canonical(string path)
		{
			path = LocalFileSystem.AbsoluteName(path);
			if (names.TryGetValue(path, out var known)) return known;
			var parent = LocalFileSystem.ParentName(path);
			if (parent == null) return path;
			parent = Canonical(parent);
			var name = LocalFileSystem.LeafName(path);
			var entries = SourceFileSystem.WithPath(parent, () => fileSystem.Entries(parent).ToArray());
			var entry = entries.FirstOrDefault(e => LocalFileSystem.LeafName(e) == name)
				?? entries.FirstOrDefault(e => string.Equals(LocalFileSystem.LeafName(e), name, StringComparison.OrdinalIgnoreCase));
			return names[path] = entry ?? LocalFileSystem.Combine(parent, name);
		}

		var files = new SortedSet<string>(pathComparer);
		var visited = new HashSet<string>(pathComparer);
		bool GeneratedName(string name) => new[] { "bin", "obj", ".git" }.Contains(name, pathComparer);
		void FileTarget(string path)
		{
			SourceFileSystem.WithPath(path, () => { fileSystem.ValidateReadable(path); return true; });
			files.Add(path);
		}
		void DirectoryTarget(string directory)
		{
			if (LocalFileSystem.Segments(directory).Any(GeneratedName)
				|| !visited.Add(directory)) return;
			SourceFileSystem.WithPath(directory, () =>
			{
				foreach (var entry in fileSystem.Entries(directory))
				{
					if (GeneratedName(LocalFileSystem.LeafName(entry))) continue;
					var attributes = SourceFileSystem.WithPath(entry, () => fileSystem.Attributes(entry));
					if ((attributes & FileAttributes.Directory) != 0)
					{
						DirectoryTarget(entry);
					}
					else if (LocalFileSystem.Extension(entry).Equals(".cs", StringComparison.OrdinalIgnoreCase)) FileTarget(entry);
				}
				return true;
			});
		}

		foreach (var target in targets.Count == 0 ? [Environment.CurrentDirectory] : targets)
		{
			SourceFileSystem.WithPath(target, () =>
			{
				var attributes = fileSystem.Attributes(LocalFileSystem.AbsoluteName(target));
				var path = Canonical(target);
				if ((attributes & FileAttributes.Directory) != 0) DirectoryTarget(path);
				else if (LocalFileSystem.Extension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)) FileTarget(path);
				else throw new ArgumentException($"Unsupported source file '{target}'; expected a .cs file or directory.");
				return true;
			});
		}
		return files.ToArray();
	}
}
