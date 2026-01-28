using System.Text.Json.Serialization;

namespace llmaid;

/// <summary>
/// Represents the settings required for configuring the application.
/// Settings can be loaded from appsettings.json, profile files (.yaml), or command line arguments.
/// </summary>
public class Settings
{
	private string? _assistantStarter;

	public static Settings Empty => new();

	[JsonIgnore]
	public bool IsEmpty => string.IsNullOrWhiteSpace(Provider);

	/// <summary>
	/// Gets or sets the provider name, which must be 'ollama', 'openai', 'lmstudio', or 'openai-compatible'.
	/// Use 'lmstudio' for LM Studio's local API (default: http://localhost:1234/v1).
	/// Use 'openai-compatible' for any other OpenAI-compatible API endpoints.
	/// </summary>
	[JsonPropertyName("provider")]
	public string? Provider { get; set; }

	/// <summary>
	/// Minimum context length for the Ollama provider to prevent model unnecessary model reloads
	/// </summary>
	[JsonPropertyName("ollamaMinNumCtx")]
	public int OllamaMinNumCtx { get; set; } = 20480;

	/// <summary>
	/// Gets or sets the API key used for authentication with the provider.
	/// Not required for Ollama or LM Studio (leave empty or use any placeholder).
	/// </summary>
	[JsonPropertyName("apiKey")]
	public string? ApiKey { get; set; }

	/// <summary>
	/// Gets or sets the URI endpoint for the API.
	/// </summary>
	[JsonPropertyName("uri")]
	public Uri? Uri { get; set; }

	/// <summary>
	/// Gets or sets the model name to be used.
	/// </summary>
	[JsonPropertyName("model")]
	public string? Model { get; set; }

	/// <summary>
	/// Gets or sets the source path where files are located that should be processed.
	/// </summary>
	[JsonPropertyName("targetPath")]
	public string? TargetPath { get; set; }

	/// <summary>
	/// Gets or sets the file glob patterns to search for.
	/// </summary>
	[JsonPropertyName("files")]
	public Files? Files { get; set; }

	/// <summary>
	/// Gets or sets the path to the profile file (.yaml) containing settings and system prompt.
	/// </summary>
	[JsonPropertyName("profile")]
	public string? Profile { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to write the models response to the console. Defaults to true.
	/// </summary>
	[JsonPropertyName("writeResponseToConsole")]
	public bool? WriteResponseToConsole { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to apply the codeblock from the model's response to the file.
	/// If true, extracts the codeblock and overwrites the file. If false, outputs the response to console.
	/// </summary>
	[JsonPropertyName("applyCodeblock")]
	public bool? ApplyCodeblock { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the operation should be a dry run.
	/// If set to <c>true</c>, the operation will simulate the actions without making any actual changes.
	/// </summary>
	[JsonPropertyName("dryRun")]
	public bool DryRun { get; set; }

	/// <summary>
	/// Gets or sets the string that should be used to start the assistant's message.
	/// Can be used to make the model think it started with the a code block already to prevent it from talking about it.
	/// </summary>
	[JsonPropertyName("assistantStarter")]
	public string? AssistantStarter
	{
		get => _assistantStarter?.Replace("\\n", Environment.NewLine);
		set => _assistantStarter = value;
	}

	/// <summary>
	/// Gets or sets the temperature value for the model.
	/// </summary>
	[JsonPropertyName("temperature")]
	public float? Temperature { get; set; }

	/// <summary>
	/// Gets or sets the system prompt to be used with the model
	/// </summary>
	[JsonPropertyName("systemPrompt")]
	public string? SystemPrompt { get; set; }

	/// <summary>
	/// Gets or sets the maximum number of retries of a reponse could not be processed
	/// </summary>
	[JsonPropertyName("maxRetries")]
	public int? MaxRetries { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to output verbose information.
	/// When false (default), only file names and responses are shown for easy processing.
	/// When true, detailed information about tokens, timing, and settings is displayed.
	/// </summary>
	[JsonPropertyName("verbose")]
	public bool? Verbose { get; set; }

	/// <summary>
	/// Gets or sets the cooldown time in seconds to wait after processing each file.
	/// This can be used to prevent processor overheating during batch processing.
	/// Default is 0 (no cooldown).
	/// </summary>
	[JsonPropertyName("cooldownSeconds")]
	public int? CooldownSeconds { get; set; }

	/// <summary>
	/// Validates the current arguments, ensuring all required fields are properly set.
	/// </summary>
	/// <param name="requireProfile">If true, validates that a profile file exists. Set to false when running purely from CLI.</param>
	public Task Validate(bool requireProfile = false)
	{
		if (string.IsNullOrEmpty(Uri?.AbsolutePath))
			throw new ArgumentException("Uri has to be defined.");

		var knownProvider = "ollama".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "openai".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "lmstudio".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "openai-compatible".Equals(Provider, StringComparison.OrdinalIgnoreCase);
		if (!knownProvider)
			throw new ArgumentException("Provider has to be 'ollama', 'openai', 'lmstudio', or 'openai-compatible'.");

		if ((Files ?? new Files([], [])).Include?.Any() == false)
			throw new ArgumentException("At least one file pattern must be defined.");

		if (string.IsNullOrEmpty(TargetPath))
			throw new ArgumentException("Target path has to be defined.");

		if (string.IsNullOrEmpty(Model))
			throw new ArgumentException("Model has to be defined.");

		if (requireProfile && (string.IsNullOrWhiteSpace(Profile) || !File.Exists(Profile)))
			throw new FileNotFoundException($"Profile file '{Profile}' does not exist.");

		if (string.IsNullOrWhiteSpace(SystemPrompt))
			throw new ArgumentException("System prompt has to be defined (either via --systemPrompt or in the profile file).");

		return Task.CompletedTask;
	}

	public void OverrideWith(Settings newSettings)
	{
		foreach (var property in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
		{
			var newValue = property.GetValue(newSettings);
			if (newValue != null && property.CanWrite)
				property.SetValue(this, newValue);
		}
	}
}