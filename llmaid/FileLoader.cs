using GlobExpressions;

namespace llmaid;

public class FileLoader : IFileLoader
{
	public string[] GetAll(string path, Files files)
	{
		return Get(path, files)
			.Distinct()
			.Order()
			.ToArray();
	}

	public IEnumerable<string> Get(string path, Files files)
	{
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

public interface IFileLoader
{
	IEnumerable<string> Get(string path, Files files);
}

public record Files(string[] Include, string[] Exclude);