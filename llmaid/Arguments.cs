namespace llmaid;

public class Arguments
{
	public required Uri Uri { get; set; }

	public required string Model { get; set; }

	public required string SourcePath { get; set; }

	public string[] FilePatterns { get; set; } = [];

	public required string PromptFile { get; set; }

	public bool WriteCodeToConsole { get; set; } = true;

	public bool ReplaceFiles { get; set; } = true;

	public float? Temperature { get; set; }

	public void Validate()
	{
		if (string.IsNullOrEmpty(Uri?.AbsolutePath))
			throw new ArgumentException("Uri has to be defined.");

		if (FilePatterns?.Any() == false)
			throw new ArgumentException("At least one file pattern must be defined.");

		if (string.IsNullOrEmpty(SourcePath))
			throw new ArgumentException("Source path has to be defined.");

		if (string.IsNullOrEmpty(Model))
			throw new ArgumentException("Model has to be defined.");

		if (string.IsNullOrWhiteSpace(PromptFile) || !File.Exists(PromptFile))
			throw new FileNotFoundException($"Prompt file '{PromptFile}' does not exist.");
	}
}
