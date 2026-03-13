using GlobExpressions;

namespace llmaid;

/// <summary>
/// Loads and filters files from the file system based on inclusion and exclusion patterns.
/// </summary>
public class FileLoader : IFileLoader
{
	/// <summary>
	/// Gets all matching files from the specified path, returning them as a sorted array with duplicates removed.
	/// </summary>
	/// <param name="path">The directory path to search for files.</param>
	/// <param name="files">The file patterns defining which files to include and exclude.</param>
	/// <returns>A sorted array of unique file paths matching the specified patterns.</returns>
	public string[] GetAll(string path, Files files)
	{
		return [.. Get(path, files)
			.Distinct()
			.Order()];
	}

	/// <summary>
	/// Gets all matching files from the specified path based on inclusion and exclusion patterns.
	/// </summary>
	/// <param name="path">The directory path to search for files.</param>
	/// <param name="files">The file patterns defining which files to include and exclude.</param>
	/// <returns>An enumerable collection of file paths matching the specified patterns.</returns>
	public IEnumerable<string> Get(string path, Files files)
	{
		// If path is a file, return it directly (ignore glob patterns)
		if (File.Exists(path))
		{
			yield return path;
			yield break;
		}

		var ignore = new Ignore.Ignore();
		ignore.Add(files.Exclude.Select(Normalize));

		foreach (var pattern in files.Include)
		{
			foreach (var entry in Glob.Files(path, pattern).Select(f => Path.Combine(path, f)))
			{
				var isExcluded = ignore.IsIgnored(Normalize(entry));
				if (!isExcluded)
					yield return entry;
			}
		}
	}

	private string Normalize(string file) => file.Replace('\\', '/');
}

/// <summary>
/// Defines the contract for loading files from the file system based on inclusion and exclusion patterns.
/// </summary>
public interface IFileLoader
{
	/// <summary>
	/// Gets all matching files from the specified path based on inclusion and exclusion patterns.
	/// </summary>
	/// <param name="path">The directory path to search for files.</param>
	/// <param name="files">The file patterns defining which files to include and exclude.</param>
	/// <returns>An enumerable collection of file paths matching the specified patterns.</returns>
	IEnumerable<string> Get(string path, Files files);
}

/// <summary>
/// Represents file inclusion and exclusion patterns for file loading operations.
/// </summary>
public class Files
{
	/// <summary>
	/// Gets or sets the array of file patterns to include.
	/// </summary>
	public string[] Include { get; set; } = [];

	/// <summary>
	/// Gets or sets the array of file patterns to exclude.
	/// </summary>
	public string[] Exclude { get; set; } = [];

	/// <summary>
	/// Initializes a new instance of the Files class with default empty arrays.
	/// </summary>
	public Files() { }

	/// <summary>
	/// Initializes a new instance of the Files class with specified include and exclude patterns.
	/// </summary>
	/// <param name="include">The array of file patterns to include.</param>
	/// <param name="exclude">The array of file patterns to exclude.</param>
	public Files(string[] include, string[] exclude)
	{
		Include = include;
		Exclude = exclude;
	}
}