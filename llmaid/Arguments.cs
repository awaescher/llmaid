namespace llmaid;

/// <summary>
/// Represents the arguments required for configuring the application.
/// </summary>
public class Arguments
{
	/// <summary>
	/// Gets or sets the provider name, which must be either 'ollama' or 'openai'.
	/// </summary>
	public required string Provider { get; set; }

	/// <summary>
	/// Gets or sets the API key used for authentication with the provider.
	/// Not required for Ollama.
	/// </summary>
	public required string ApiKey { get; set; }

	/// <summary>
	/// Gets or sets the URI endpoint for the API.
	/// </summary>
	public required Uri Uri { get; set; }

	/// <summary>
	/// Gets or sets the model name to be used.
	/// </summary>
	public required string Model { get; set; }

	/// <summary>
	/// Gets or sets the source path where files are located that should be processed.
	/// </summary>
	public required string SourcePath { get; set; }

	/// <summary>
	/// Gets or sets the file patterns to search for.
	/// </summary>
	public string[] FilePatterns { get; set; } = [];

	/// <summary>
	/// Gets or sets the path to the file defining the system prompt.
	/// </summary>
	public required string PromptFile { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to write code to the console. Defaults to true.
	/// </summary>
	public bool WriteCodeToConsole { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to replace files in the source folder. Defaults to true.
	/// </summary>
	public bool ReplaceFiles { get; set; } = true;

	/// <summary>
	/// Gets or sets the temperature value for the model.
	/// </summary>
	public float? Temperature { get; set; }

	/// <summary>
	/// Validates the current arguments, ensuring all required fields are properly set.
	/// </summary>
	public void Validate()
	{
		if (string.IsNullOrEmpty(Uri?.AbsolutePath))
			throw new ArgumentException("Uri has to be defined.");

		var knownProvider = "ollama".Equals(Provider, StringComparison.OrdinalIgnoreCase) || "openai".Equals(Provider, StringComparison.OrdinalIgnoreCase);
		if (!knownProvider)
			throw new ArgumentException("Provider has to be 'ollama' or 'openai'.");

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
