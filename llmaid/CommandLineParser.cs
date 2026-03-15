using System.CommandLine;

namespace llmaid;

/// <summary>
/// Parses command-line arguments into a <see cref="Settings"/> instance.
/// Each option maps to a property on <see cref="Settings"/> and follows the --camelCase convention.
/// </summary>
internal static class CommandLineParser
{
	/// <summary>
	/// Parses command-line arguments into a <see cref="Settings"/> instance.
	/// </summary>
	/// <param name="args">The command-line arguments to parse.</param>
	/// <returns>A populated <see cref="Settings"/> instance with values from the command-line arguments.</returns>
	public static Settings Parse(string[] args)
	{
		static string MakeArgument(string value) => $"--{char.ToLowerInvariant(value[0])}{value[1..]}";

		var settings = new Settings();

		// --- Define options ---

		var providerOption = new Option<string>(MakeArgument(nameof(Settings.Provider)), "The provider name");
		var apiKeyOption = new Option<string>(MakeArgument(nameof(Settings.ApiKey)), "The API key used for authentication");
		var uriOption = new Option<string>(MakeArgument(nameof(Settings.Uri)), "The URI endpoint for the API");
		var modelOption = new Option<string>(MakeArgument(nameof(Settings.Model)), "The model name to be used");
		var targetPathOption = new Option<string>(MakeArgument(nameof(Settings.TargetPath)), "The source path where files are located");
		var profileOption = new Option<string>("--profile", "The path to the profile file (.yaml) containing settings and system prompt");
		var writeResponseToConsoleOption = new Option<bool>(MakeArgument(nameof(Settings.WriteResponseToConsole)), "Whether to write the model's response to the console");
		var dryRunOption = new Option<bool>(MakeArgument(nameof(Settings.DryRun)), "Simulate processing without making actual changes");
		var assistantStarterOption = new Option<string>(MakeArgument(nameof(Settings.AssistantStarter)), "The string to start the assistant's message");
		var temperatureOption = new Option<float?>(MakeArgument(nameof(Settings.Temperature)), "The temperature value for the model");
		var systemPromptOption = new Option<string>(MakeArgument(nameof(Settings.SystemPrompt)), "The system prompt to be used with the model");
		var maxRetriesOption = new Option<int>(MakeArgument(nameof(Settings.MaxRetries)), "The maximum number of retries if a response could not be processed");
		var cooldownSecondsOption = new Option<int?>(MakeArgument(nameof(Settings.CooldownSeconds)), "Cooldown time in seconds after processing each file (prevents overheating)");
		var maxFileTokensOption = new Option<int?>(MakeArgument(nameof(Settings.MaxFileTokens)), "Maximum number of tokens a file may contain before it is skipped (default: 102400)");
		var resumeAtOption = new Option<string>(MakeArgument(nameof(Settings.ResumeAt)), "Resume processing from a specific file (skips all files until a filename containing this pattern is found)");
		var ollamaMinNumCtxOption = new Option<int?>(MakeArgument(nameof(Settings.OllamaMinNumCtx)), "Minimum context length for Ollama provider to prevent unnecessary model reloads (default: 20480)");
		var reasoningTimeoutOption = new Option<int?>(MakeArgument(nameof(Settings.ReasoningTimeoutSeconds)), "Maximum seconds a model may spend reasoning before the request is cancelled (default: 600, 0 = disabled)");
		var maxImageDimensionOption = new Option<int?>(MakeArgument(nameof(Settings.MaxImageDimension)), "Maximum image dimension (width or height) in pixels, images are resized to fit (default: 2048)");
		var judgeMaxRetriesOption = new Option<int?>(MakeArgument(nameof(Settings.JudgeMaxRetries)), "Maximum number of judge review cycles per file. The judge evaluates the LLM response or git diff against the task instructions and triggers a retry with specific violation feedback when rejected. Set to 0 or omit to disable.");
		var judgeModeOption = new Option<string>(MakeArgument(nameof(Settings.JudgeMode)), "Judge evaluation mode: 'response' (default) evaluates the raw LLM response before writing — works always, no git required; 'git-diff' evaluates the actual git diff after writing — requires applyCodeblock=true and a git repository; 'both' chains response-judge before write and git-diff judge after write.");
		var judgeSystemPromptOption = new Option<string>(MakeArgument(nameof(Settings.JudgeSystemPrompt)), "System prompt for the judge LLM. When not set, a built-in default prompt is used.");
		var judgeModelOption = new Option<string>(MakeArgument(nameof(Settings.JudgeModel)), "Model for judge calls. Only specify when the judge should use a different model than the main editing model. Falls back to --model when not set.");
		var judgeProviderOption = new Option<string>(MakeArgument(nameof(Settings.JudgeProvider)), "Provider for judge calls ('ollama', 'openai', 'lmstudio', or 'openai-compatible'). Only specify when the judge should use a different provider. Falls back to --provider when not set.");
		var judgeUriOption = new Option<string>(MakeArgument(nameof(Settings.JudgeUri)), "API endpoint URI for judge calls. Only specify when the judge should connect to a different server. Falls back to --uri when not set.");
		var judgeApiKeyOption = new Option<string>(MakeArgument(nameof(Settings.JudgeApiKey)), "API key for judge calls. Only specify when the judge provider requires a different key. Falls back to --apiKey when not set.");

		// Toggle flags: can be specified without a value (defaults to true when present)
		var applyCodeblockOption = new Option<bool?>(MakeArgument(nameof(Settings.ApplyCodeblock)), "Extract codeblock from response and overwrite file (false = output to console)");
		applyCodeblockOption.Arity = ArgumentArity.ZeroOrOne;

		var verboseOption = new Option<bool?>(MakeArgument(nameof(Settings.Verbose)), "Show detailed output including tokens and timing information");
		verboseOption.Arity = ArgumentArity.ZeroOrOne;

		var diagnosticOption = new Option<bool?>(MakeArgument(nameof(Settings.Diagnostic)), "Print full system prompt, user message, and LLM response for every LLM call (implies --verbose)");
		diagnosticOption.Arity = ArgumentArity.ZeroOrOne;

		var preserveWhitespaceOption = new Option<bool?>(MakeArgument(nameof(Settings.PreserveWhitespace)), "Preserve original leading and trailing whitespace to avoid diff noise (default: false)");
		preserveWhitespaceOption.Arity = ArgumentArity.ZeroOrOne;

		var showProgressOption = new Option<bool?>(MakeArgument(nameof(Settings.ShowProgress)), "Show progress indicator during file processing (default: true)");
		showProgressOption.Arity = ArgumentArity.ZeroOrOne;

		// --- Build root command ---

		var rootCommand = new RootCommand
		{
			providerOption,
			apiKeyOption,
			uriOption,
			modelOption,
			targetPathOption,
			profileOption,
			writeResponseToConsoleOption,
			applyCodeblockOption,
			dryRunOption,
			assistantStarterOption,
			temperatureOption,
			systemPromptOption,
			maxRetriesOption,
			verboseOption,
			diagnosticOption,
			cooldownSecondsOption,
			maxFileTokensOption,
			resumeAtOption,
			ollamaMinNumCtxOption,
			preserveWhitespaceOption,
			showProgressOption,
			reasoningTimeoutOption,
			maxImageDimensionOption,
			judgeMaxRetriesOption,
			judgeModeOption,
			judgeSystemPromptOption,
			judgeModelOption,
			judgeProviderOption,
			judgeUriOption,
			judgeApiKeyOption
		};

		// --- Bind parsed values to settings ---

		rootCommand.SetHandler(context =>
		{
			settings.Provider = context.ParseResult.GetValueForOption(providerOption);
			settings.ApiKey = context.ParseResult.GetValueForOption(apiKeyOption);
			settings.Uri = context.ParseResult.GetValueForOption(uriOption) is string uri && !string.IsNullOrWhiteSpace(uri) ? new Uri(uri) : null;
			settings.Model = context.ParseResult.GetValueForOption(modelOption);
			settings.TargetPath = context.ParseResult.GetValueForOption(targetPathOption);
			settings.Profile = context.ParseResult.GetValueForOption(profileOption);
			settings.WriteResponseToConsole = context.ParseResult.GetValueForOption(writeResponseToConsoleOption);
			settings.DryRun = context.ParseResult.GetValueForOption(dryRunOption);
			settings.AssistantStarter = context.ParseResult.GetValueForOption(assistantStarterOption);
			settings.Temperature = context.ParseResult.GetValueForOption(temperatureOption);
			settings.SystemPrompt = context.ParseResult.GetValueForOption(systemPromptOption);
			settings.MaxRetries = context.ParseResult.GetValueForOption(maxRetriesOption);
			settings.CooldownSeconds = context.ParseResult.GetValueForOption(cooldownSecondsOption);
			settings.MaxFileTokens = context.ParseResult.GetValueForOption(maxFileTokensOption);
			settings.ResumeAt = context.ParseResult.GetValueForOption(resumeAtOption);
			settings.OllamaMinNumCtx = context.ParseResult.GetValueForOption(ollamaMinNumCtxOption) ?? settings.OllamaMinNumCtx;
			settings.ReasoningTimeoutSeconds = context.ParseResult.GetValueForOption(reasoningTimeoutOption) ?? settings.ReasoningTimeoutSeconds;
			settings.MaxImageDimension = context.ParseResult.GetValueForOption(maxImageDimensionOption) ?? settings.MaxImageDimension;
			settings.JudgeMaxRetries = context.ParseResult.GetValueForOption(judgeMaxRetriesOption);
			settings.JudgeMode = context.ParseResult.GetValueForOption(judgeModeOption);
			settings.JudgeSystemPrompt = context.ParseResult.GetValueForOption(judgeSystemPromptOption);
			settings.JudgeModel = context.ParseResult.GetValueForOption(judgeModelOption);
			settings.JudgeProvider = context.ParseResult.GetValueForOption(judgeProviderOption);
			settings.JudgeUri = context.ParseResult.GetValueForOption(judgeUriOption) is string judgeUri && !string.IsNullOrWhiteSpace(judgeUri) ? new Uri(judgeUri) : null;
			settings.JudgeApiKey = context.ParseResult.GetValueForOption(judgeApiKeyOption);

			// Toggle flags: when specified without value, they default to true
			if (context.ParseResult.FindResultFor(applyCodeblockOption) is not null)
				settings.ApplyCodeblock = context.ParseResult.GetValueForOption(applyCodeblockOption) ?? true;

			if (context.ParseResult.FindResultFor(verboseOption) is not null)
				settings.Verbose = context.ParseResult.GetValueForOption(verboseOption) ?? true;

			if (context.ParseResult.FindResultFor(diagnosticOption) is not null)
				settings.Diagnostic = context.ParseResult.GetValueForOption(diagnosticOption) ?? true;

			if (context.ParseResult.FindResultFor(preserveWhitespaceOption) is not null)
				settings.PreserveWhitespace = context.ParseResult.GetValueForOption(preserveWhitespaceOption) ?? true;

			if (context.ParseResult.FindResultFor(showProgressOption) is not null)
				settings.ShowProgress = context.ParseResult.GetValueForOption(showProgressOption) ?? true;

			context.ExitCode = 0;
		});

		rootCommand.Invoke(args);

		return settings;
	}
}