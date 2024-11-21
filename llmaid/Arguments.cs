using System.Text.Json;

namespace llmaid;

/// <summary>
/// Represents the arguments required for configuring the application.
/// </summary>
public class Arguments
{
	private const string FIND_MODE = "find";
	private const string REPLACEFILE_MODE = "replacefile";
	private string _assistantStarter = string.Empty;

	/// <summary>
	/// Gets whether llmaid is in find mode, where file contents are not changed
	/// </summary>
	public bool IsFindMode => Mode == FIND_MODE;

	/// <summary>
	/// Gets whether llmaid is in replacefile method where file contents are being replaced
	/// </summary>
	public bool IsReplaceMode => Mode == REPLACEFILE_MODE;

	/// <summary>
	/// Gets or sets the provider name, which must be either 'ollama' or 'openai'.
	/// </summary>
	public string Provider { get; set; }

	/// <summary>
	/// Gets or sets the API key used for authentication with the provider.
	/// Not required for Ollama.
	/// </summary>
	public string ApiKey { get; set; }

	/// <summary>
	/// Gets or sets the URI endpoint for the API.
	/// </summary>
	public Uri Uri { get; set; }

	/// <summary>
	/// Gets or sets the model name to be used.
	/// </summary>
	public string Model { get; set; }

	/// <summary>
	/// Gets or sets the source path where files are located that should be processed.
	/// </summary>
	public string TargetPath { get; set; }

	/// <summary>
	/// Gets or sets the file glob patterns to search for.
	/// </summary>
	public Files Files { get; set; } = new([], []);

	/// <summary>
	/// Gets or sets the path to the file defining the system prompt.
	/// </summary>
	public string DefinitionFile { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to write the models response to the console. Defaults to true.
	/// </summary>
	public bool WriteResponseToConsole { get; set; } = true;

	/// <summary>
	/// Gets or sets the mode in which llmaid is operating, like only finding text or replacing it
	/// </summary>
	public string Mode { get; set; } = REPLACEFILE_MODE;

	/// <summary>
	/// Gets or sets the string that should be used to start the assistant's message.
	/// Can be used to make the model think it started with the a code block already to prevent it from talking about it.
	/// </summary>
	public string AssistantStarter
	{
		get => _assistantStarter?.Replace("\\n", Environment.NewLine) ?? "";
		set => _assistantStarter = value;
	}

	/// <summary>
	/// Gets or sets the temperature value for the model.
	/// </summary>
	public float? Temperature { get; set; }

	/// <summary>
	/// Gets or sets the system prompt to be used with the model
	/// </summary>
	public string SystemPrompt { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the maximum number of retries of a reponse could not be processed
	/// </summary>
	public int MaxRetries { get; set; }

	/// <summary>
	/// Validates the current arguments, ensuring all required fields are properly set.
	/// </summary>
	public async Task Validate()
	{
		await OverrideArgumentsFromDefinition();

		if (string.IsNullOrEmpty(Uri?.AbsolutePath))
			throw new ArgumentException("Uri has to be defined.");

		var knownProvider = "ollama".Equals(Provider, StringComparison.OrdinalIgnoreCase) || "openai".Equals(Provider, StringComparison.OrdinalIgnoreCase);
		if (!knownProvider)
			throw new ArgumentException("Provider has to be 'ollama' or 'openai'.");

		var knownMode = FIND_MODE.Equals(Mode, StringComparison.OrdinalIgnoreCase) || REPLACEFILE_MODE.Equals(Mode, StringComparison.OrdinalIgnoreCase);
		if (!knownMode)
			throw new ArgumentException("Mode has to be 'find' or 'replacefile'.");

		if (Files.Include?.Any() == false)
			throw new ArgumentException("At least one file pattern must be defined.");

		if (string.IsNullOrEmpty(TargetPath))
			throw new ArgumentException("Target path has to be defined.");

		if (string.IsNullOrEmpty(Model))
			throw new ArgumentException("Model has to be defined.");

		if (string.IsNullOrWhiteSpace(DefinitionFile) || !File.Exists(DefinitionFile))
			throw new FileNotFoundException($"Prompt file '{DefinitionFile}' does not exist.");
	}

	private async Task OverrideArgumentsFromDefinition()
	{
		const string XML_TAG = "OverrideSettings";

		// keep a local variable as the SystemPrompt might get overridden in UpdateExistingArguments()
		var systemPrompt = await File.ReadAllTextAsync(DefinitionFile).ConfigureAwait(false);
		SystemPrompt = systemPrompt;

		var codeBlock = CodeBlockExtractor.Extract(SystemPrompt, XML_TAG);
		if (codeBlock.Trim().Any())
		{
			// overwrite settings
			UpdateExistingArguments(JsonSerializer.Deserialize<Arguments>(codeBlock, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip }));

			SystemPrompt = systemPrompt.Split($"</{XML_TAG}>").Skip(1).Take(1).Single().TrimStart();
		}
	}

	private void UpdateExistingArguments(Arguments newArguments)
	{
		foreach (var property in typeof(Arguments).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
		{
			var newValue = property.GetValue(newArguments);
			if (newValue != null && property.CanWrite)
				property.SetValue(this, newValue);
		}
	}
}