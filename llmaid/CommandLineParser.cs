using System.CommandLine;

namespace llmaid;

/// <summary>
/// Parses command-line arguments into a <see cref="Settings"/> instance.
/// Each option maps to a property on <see cref="Settings"/> and follows the --camelCase convention.
/// </summary>
internal static class CommandLineParser
{
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

		// Toggle flags: can be specified without a value (defaults to true when present)
		var applyCodeblockOption = new Option<bool?>(MakeArgument(nameof(Settings.ApplyCodeblock)), "Extract codeblock from response and overwrite file (false = output to console)");
		applyCodeblockOption.Arity = ArgumentArity.ZeroOrOne;

		var verboseOption = new Option<bool?>(MakeArgument(nameof(Settings.Verbose)), "Show detailed output including tokens and timing information");
		verboseOption.Arity = ArgumentArity.ZeroOrOne;

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
			cooldownSecondsOption,
			maxFileTokensOption,
			resumeAtOption,
			ollamaMinNumCtxOption,
			preserveWhitespaceOption,
			showProgressOption,
			reasoningTimeoutOption
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

			// Toggle flags: when specified without value, they default to true
			if (context.ParseResult.FindResultFor(applyCodeblockOption) is not null)
				settings.ApplyCodeblock = context.ParseResult.GetValueForOption(applyCodeblockOption) ?? true;

			if (context.ParseResult.FindResultFor(verboseOption) is not null)
				settings.Verbose = context.ParseResult.GetValueForOption(verboseOption) ?? true;

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
