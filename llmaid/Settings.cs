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
	/// Gets or sets the provider name, which must be 'ollama', 'openai', 'lmstudio', 'openai-compatible', or 'minimax'.
	/// Use 'lmstudio' for LM Studio's local API (default: http://localhost:1234/v1).
	/// Use 'openai-compatible' for any other OpenAI-compatible API endpoints.
	/// Use 'minimax' for MiniMax's API (default: https://api.minimax.io/v1).
	/// </summary>
	[JsonPropertyName("provider")]
	public string? Provider { get; set; }

	/// <summary>
	/// Gets or sets the minimum context length for the Ollama provider to prevent unnecessary model reloads.
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
	/// Gets or sets the URI endpoint for the API connection.
	/// </summary>
	[JsonPropertyName("uri")]
	public Uri? Uri { get; set; }

	/// <summary>
	/// Gets or sets the model name to be used for generating responses.
	/// </summary>
	[JsonPropertyName("model")]
	public string? Model { get; set; }

	/// <summary>
	/// Gets or sets the source path where files are located that should be processed.
	/// Can point to a single file or a directory containing files to process.
	/// </summary>
	[JsonPropertyName("targetPath")]
	public string? TargetPath { get; set; }

	/// <summary>
	/// Gets or sets the file glob patterns to search for when processing a directory.
	/// </summary>
	[JsonPropertyName("files")]
	public Files? Files { get; set; }

	/// <summary>
	/// Gets or sets the path to the profile file (.yaml) containing settings and system prompt.
	/// </summary>
	[JsonPropertyName("profile")]
	public string? Profile { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to write the model's response to the console. Defaults to true.
	/// </summary>
	[JsonPropertyName("writeResponseToConsole")]
	public bool? WriteResponseToConsole { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to apply the code block from the model's response to the file.
	/// If true, extracts the code block and overwrites the file. If false, outputs the response to console only.
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
	/// Gets or sets a pattern to resume processing from a specific file.
	/// All files will be skipped until a filename containing this pattern is found.
	/// Use this to continue processing from where you left off after an interruption.
	/// </summary>
	[JsonPropertyName("resumeAt")]
	public string? ResumeAt { get; set; }

	/// <summary>
	/// Gets or sets the string that should be used to start the assistant's message.
	/// Can be used to make the model think it started with a code block already to prevent it from talking about it.
	/// </summary>
	[JsonPropertyName("assistantStarter")]
	public string? AssistantStarter
	{
		get => _assistantStarter?.Replace("\\n", Environment.NewLine);
		set => _assistantStarter = value;
	}

	/// <summary>
	/// Gets or sets the temperature value for the model, controlling randomness in responses.
	/// Higher values produce more creative output; lower values produce more deterministic results.
	/// </summary>
	[JsonPropertyName("temperature")]
	public float? Temperature { get; set; }

	/// <summary>
	/// Gets or sets the system prompt to be used with the model.
	/// Defines the behavior and constraints for the AI assistant.
	/// </summary>
	[JsonPropertyName("systemPrompt")]
	public string? SystemPrompt { get; set; }

	/// <summary>
	/// Gets or sets the maximum number of retries when a response could not be processed.
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
	/// Gets or sets a value indicating whether to output diagnostic information.
	/// When true, the full system prompt, user message, and LLM response are printed
	/// for every LLM call (both the editor and the judge). Implies verbose mode.
	/// </summary>
	[JsonPropertyName("diagnostic")]
	public bool? Diagnostic { get; set; }

	/// <summary>
	/// Gets or sets the cooldown time in seconds to wait after processing each file.
	/// This can be used to prevent processor overheating during batch processing.
	/// Default is 0 (no cooldown).
	/// </summary>
	[JsonPropertyName("cooldownSeconds")]
	public int? CooldownSeconds { get; set; }

	/// <summary>
	/// Gets or sets the maximum number of tokens a file may contain before it is skipped.
	/// Files exceeding this limit will not be processed. Default is 102400.
	/// </summary>
	[JsonPropertyName("maxFileTokens")]
	public int? MaxFileTokens { get; set; } = 102400;

	/// <summary>
	/// Gets or sets a value indicating whether to preserve the original leading and trailing whitespace
	/// when writing back the processed code. This helps avoid diff noise from whitespace changes.
	/// Default is false.
	/// </summary>
	[JsonPropertyName("preserveWhitespace")]
	public bool? PreserveWhitespace { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to show the progress indicator during file processing.
	/// Set to false to hide the progress bar, which can be useful for scripting or CI/CD pipelines.
	/// Default is true.
	/// </summary>
	[JsonPropertyName("showProgress")]
	public bool? ShowProgress { get; set; } = true;

	/// <summary>
	/// Gets or sets the maximum number of seconds a model is allowed to spend in the reasoning phase
	/// before the request is cancelled. This is not an HTTP timeout — it only kicks in when no output
	/// tokens have been received yet (i.e. the model is still reasoning).
	/// Default is 600 seconds (10 minutes). Set to 0 to disable.
	/// </summary>
	[JsonPropertyName("reasoningTimeoutSeconds")]
	public int? ReasoningTimeoutSeconds { get; set; } = 600;

	/// <summary>
	/// Gets or sets the maximum dimension (width or height) for images.
	/// Images are always resized to fit within this dimension while preserving aspect ratio.
	/// Default is 2048 pixels.
	/// </summary>
	[JsonPropertyName("maxImageDimension")]
	public int? MaxImageDimension { get; set; } = 2048;

	// ── Judge settings ────────────────────────────────────────────────────────
	// The judge is an optional second LLM call that verifies the AI's output
	// against the task instructions and triggers a retry with specific violation
	// feedback when rejected. Two evaluation modes are available:
	//
	//   • "response"  — evaluates the raw LLM response before writing (default).
	//                   Works always, even without git or applyCodeblock.
	//   • "git-diff"  — evaluates the actual git diff after writing.
	//                   Requires applyCodeblock=true and a git repository.
	//   • "both"      — response-judge first, then git-diff-judge after writing.
	//                   Requires applyCodeblock=true and a git repository for the
	//                   git-diff step.
	//
	// All judge-specific connection settings below are optional and fall back to
	// the corresponding main-provider values when not specified.

	/// <summary>
	/// Gets or sets the maximum number of judge review cycles per file.
	/// When the judge rejects the changes, the LLM is asked to retry with the
	/// judge's specific violation feedback.
	/// Set to 0 or leave empty to disable the judge entirely.
	/// </summary>
	[JsonPropertyName("judgeMaxRetries")]
	public int? JudgeMaxRetries { get; set; }

	/// <summary>
	/// Gets or sets the judge evaluation mode.
	/// <list type="bullet">
	///   <item><term>response</term><description>Judge evaluates the raw LLM response before writing. Works always, even without git or applyCodeblock (default).</description></item>
	///   <item><term>git-diff</term><description>Judge evaluates the actual git diff after writing. Requires <see cref="ApplyCodeblock"/> to be true and the target files to reside inside a git repository.</description></item>
	///   <item><term>both</term><description>Response-judge runs first (before writing), then git-diff-judge runs after writing. Git-diff step requires <see cref="ApplyCodeblock"/> and a git repository.</description></item>
	/// </list>
	/// When not specified, defaults to <c>response</c>.
	/// </summary>
	[JsonPropertyName("judgeMode")]
	public string? JudgeMode { get; set; }

	/// <summary>
	/// Gets or sets the system prompt used by the judge LLM.
	/// The judge receives this prompt together with the original task instructions
	/// and either the raw LLM response or the git diff (depending on
	/// <see cref="JudgeMode"/>), and must respond with PASS or FAIL followed by
	/// a bullet list of violations.
	/// When not specified, a built-in default prompt is used.
	/// </summary>
	[JsonPropertyName("judgeSystemPrompt")]
	public string? JudgeSystemPrompt { get; set; }

	/// <summary>
	/// Gets or sets the model used for judge calls.
	/// Only specify this when you want to use a different model than the main
	/// editing model (e.g. a larger or more capable model for stricter review).
	/// Falls back to <see cref="Model"/> when not specified.
	/// </summary>
	[JsonPropertyName("judgeModel")]
	public string? JudgeModel { get; set; }

	/// <summary>
	/// Gets or sets the provider used for judge calls ('ollama', 'openai',
	/// 'lmstudio', or 'openai-compatible').
	/// Only specify this when the judge should use a different provider than the
	/// main editing provider (e.g. a cloud API for judging while editing runs
	/// locally).
	/// Falls back to <see cref="Provider"/> when not specified.
	/// </summary>
	[JsonPropertyName("judgeProvider")]
	public string? JudgeProvider { get; set; }

	/// <summary>
	/// Gets or sets the API endpoint URI for judge calls.
	/// Only specify this when the judge should connect to a different server than
	/// the main editing provider.
	/// Falls back to <see cref="Uri"/> when not specified.
	/// </summary>
	[JsonPropertyName("judgeUri")]
	public Uri? JudgeUri { get; set; }

	/// <summary>
	/// Gets or sets the API key for judge calls.
	/// Only specify this when the judge provider requires a different key than the
	/// main editing provider.
	/// Falls back to <see cref="ApiKey"/> when not specified.
	/// </summary>
	[JsonPropertyName("judgeApiKey")]
	public string? JudgeApiKey { get; set; }

	/// <summary>
	/// Validates the current arguments, ensuring all required fields are properly set.
	/// </summary>
	/// <param name="requireProfile">If true, validates that a profile file exists. Set to false when running purely from CLI.</param>
	/// <returns>A task representing the validation operation. Throws exceptions if validation fails.</returns>
	public Task Validate(bool requireProfile = false)
	{
		if (string.IsNullOrEmpty(Uri?.AbsolutePath))
			throw new ArgumentException("Uri has to be defined.");

		var knownProvider = "ollama".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "openai".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "lmstudio".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "openai-compatible".Equals(Provider, StringComparison.OrdinalIgnoreCase)
			|| "minimax".Equals(Provider, StringComparison.OrdinalIgnoreCase);
		if (!knownProvider)
			throw new ArgumentException("Provider has to be 'ollama', 'openai', 'lmstudio', 'openai-compatible', or 'minimax'.");

		if (string.IsNullOrEmpty(TargetPath))
			throw new ArgumentException("Target path has to be defined.");

		// File patterns are only required when TargetPath is a directory, not when it's a file
		var isFile = File.Exists(TargetPath);
		if (!isFile && (Files ?? new Files([], [])).Include?.Any() == false)
			throw new ArgumentException("At least one file pattern must be defined.");

		if (string.IsNullOrEmpty(Model))
			throw new ArgumentException("Model has to be defined.");

		if (requireProfile && (string.IsNullOrWhiteSpace(Profile) || !File.Exists(Profile)))
			throw new FileNotFoundException($"Profile file '{Profile}' does not exist.");

		if (string.IsNullOrWhiteSpace(SystemPrompt))
			throw new ArgumentException("System prompt has to be defined (either via --systemPrompt or in the profile file).");

		return Task.CompletedTask;
	}

	/// <summary>
	/// Overrides the current settings with values from another Settings instance.
	/// Only non-null property values are copied, allowing partial updates without overwriting existing configuration.
	/// </summary>
	/// <param name="newSettings">The settings instance containing values to copy from.</param>
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