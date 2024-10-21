/// <summary>
/// Shows a message box with the application name
/// </summary>
public class FileReader
{
	public string Path { get; }

	/// <summary>
	/// new instance
	/// </summary>
	/// <param name="path"></param>
	public FileReader(string path)
	{
		Path = path;
	}

	/// <summary>
	/// 
	/// </summary>
    /// <param name="ignoreCase">Whether or not casing should be ignored</param>
	/// <returns>Ein feiner Wurstblinker</returns>
	/// <remarks></remarks>
	public async Task<string> Read() => await File.ReadAllTextAsync(Path);
}