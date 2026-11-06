namespace RaiGuard;

/// <summary>Read/validation boundary; production always uses the real filesystem.</summary>
internal class SourceFileSystem
{
	internal virtual FileAttributes Attributes(string path) => File.GetAttributes(path);
	internal virtual IEnumerable<string> Entries(string directory) => Directory.EnumerateFileSystemEntries(directory);
	internal virtual void ValidateReadable(string path)
	{
		using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
	}
	internal virtual string ReadAllText(string path) => WithPath(path, () => File.ReadAllText(path));
	internal virtual void WriteAllText(string path, string content) => WithPath(path, () => { File.WriteAllText(path, content); return true; });
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
			path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
			if (names.TryGetValue(path, out var known)) return known;
			var parent = Path.GetDirectoryName(path);
			if (parent == null) return path;
			parent = Canonical(parent);
			var name = Path.GetFileName(path);
			var entries = SourceFileSystem.WithPath(parent, () => fileSystem.Entries(parent).ToArray());
			var entry = entries.FirstOrDefault(e => Path.GetFileName(e) == name)
				?? entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
			return names[path] = entry ?? Path.Combine(parent, name);
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
			if (directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(GeneratedName)
				|| !visited.Add(directory)) return;
			SourceFileSystem.WithPath(directory, () =>
			{
				foreach (var entry in fileSystem.Entries(directory))
				{
					if (GeneratedName(Path.GetFileName(entry))) continue;
					var attributes = SourceFileSystem.WithPath(entry, () => fileSystem.Attributes(entry));
					if ((attributes & FileAttributes.Directory) != 0)
					{
						DirectoryTarget(entry);
					}
					else if (Path.GetExtension(entry).Equals(".cs", StringComparison.OrdinalIgnoreCase)) FileTarget(entry);
				}
				return true;
			});
		}

		foreach (var target in targets.Count == 0 ? [Directory.GetCurrentDirectory()] : targets)
		{
			SourceFileSystem.WithPath(target, () =>
			{
				var attributes = fileSystem.Attributes(Path.GetFullPath(target));
				var path = Canonical(target);
				if ((attributes & FileAttributes.Directory) != 0) DirectoryTarget(path);
				else if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)) FileTarget(path);
				else throw new ArgumentException($"Unsupported source file '{target}'; expected a .cs file or directory.");
				return true;
			});
		}
		return files.ToArray();
	}
}
