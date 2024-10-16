namespace llmaid;

public class FileLoader : IFileLoader
{
	public IEnumerable<string> Get(string path, string[] searchPatterns)
	{
		foreach (var pattern in searchPatterns)
		{
			foreach (var entry in Directory.GetFileSystemEntries(path, pattern, SearchOption.AllDirectories))
			{
				yield return entry;
			}
		}
	}
}

public interface IFileLoader
{
	IEnumerable<string> Get(string path, string[] searchPatterns);
}