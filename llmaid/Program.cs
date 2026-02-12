using System.ClientModel;
using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.Tokenizers;
using OllamaSharp;
using OllamaSharp.Models;
using Serilog;
using Spectre.Console;
using UtfUnknown;
namespace llmaid;

internal static class Program
{
	internal static IChatClient ChatClient { get; set; } = null!;
	internal static int FileCount { get; set; }
	internal static int CurrentFileIndex { get; set; }
	internal static bool Verbose { get; set; }

	// Cumulative token tracking across all files
	private static long _cumulativeInputTokens;
	private static long _cumulativeOutputTokens;
	private static long _cumulativeReasoningTokens;
	private static long _cumulativeTotalTokens;
	private static Stopwatch _totalStopwatch = new();

	// Tokenizer for accurate token estimation (o200k_base is the standard for GPT-4o and similar models)

	private static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

	static async Task Main(string[] args)
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		var cancellationToken = cancellationTokenSource.Token;

		// Settings loading order (each layer can override previous values):
		// 1. appsettings.json - base configuration
		// 2. Profile file (.yaml) - task-specific settings and system prompt
		// 3. Command line arguments - runtime overrides (highest priority)

		var config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
			.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
			.AddJsonFile($"appsettings.{(Debugger.IsAttached ? "VisualStudio" : "Production")}.json", optional: true)
			.Build();

		// Layer 1: Load base settings from appsettings.json
		var settings = config.Get<Settings>() ?? throw new ArgumentException("Settings could not be parsed.");

		// Parse CLI args early to get profile path (but don't apply other CLI args yet)
		var cliSettings = ParseCommandLine(args);
		var profilePath = cliSettings.Profile ?? settings.Profile;

		// Layer 2: Override with profile file settings (if provided)
		if (!string.IsNullOrWhiteSpace(profilePath))
			settings.OverrideWith(await ParseProfileFile(profilePath));

		// Layer 3: Override with CLI arguments (highest priority)
		settings.OverrideWith(cliSettings);

		await settings.Validate(requireProfile: false);

		Verbose = settings.Verbose ?? false;

		// Initialize cumulative token counters
		_cumulativeInputTokens = 0;
		_cumulativeOutputTokens = 0;
		_cumulativeReasoningTokens = 0;
		_cumulativeTotalTokens = 0;

		Log.Logger = new LoggerConfiguration()
			.WriteTo.File($"./logs/{DateTime.Now:yyyy-MM-dd-hh-mm-ss}.log")
			.MinimumLevel.Verbose()
			.CreateLogger();

		ChatClient = CreateChatClient(settings);

		LogVerboseInfo($"Running {settings.Model} ({settings.Uri}) against {settings.TargetPath}." + Environment.NewLine);
		LogVerboseDetail(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

		var loader = new FileLoader();
		_totalStopwatch = Stopwatch.StartNew();

		LogVerboseInfo("Locating all files ..." + Environment.NewLine);
		var files = loader.GetAll(settings.TargetPath ?? string.Empty, settings.Files ?? new Files([], []));
		FileCount = files.Length;

		var errors = 0;
		var hasResumed = string.IsNullOrWhiteSpace(settings.ResumeAt);

		foreach (var file in files)
		{
			CurrentFileIndex++;

			// Check if we need to skip files until we find the resume pattern
			if (!hasResumed)
			{
				if (file.Contains(settings.ResumeAt!, StringComparison.OrdinalIgnoreCase))
				{
					hasResumed = true;
				}
				else
				{
					LogVerboseDetail($"[{CurrentFileIndex}/{FileCount}] Skipping (resume): {file}");
					continue;
				}
			}

			var fileHeader = $"[{CurrentFileIndex}/{FileCount}] {file} ({GetFileSizeString(file)})";
			LogFileHeader(fileHeader);

			var success = false;
			var attempt = 0;
			do
			{
				attempt++;
				if (attempt > 1)
					LogVerboseDetail("Attempt " + attempt);

				var retryMessage = attempt > 1 ? $"This is the {attempt}. attempt to process this file, all prior attempts failed because your response could not be processed. Please follow the instructions more closely." : "";
				if (settings.DryRun)
					success = true;
				else
					success = await ProcessFile(file, settings.SystemPrompt ?? string.Empty, retryMessage, settings, cancellationToken);

			} while (!success && attempt < settings.MaxRetries);

			if (!success)
				errors++;

			// Apply cooldown after processing each file (if configured)
			var cooldownSeconds = settings.CooldownSeconds ?? 0;
			if (cooldownSeconds > 0 && !settings.DryRun)
			{
				LogVerboseDetail($"Cooling down for {cooldownSeconds} seconds...");
				await Task.Delay(TimeSpan.FromSeconds(cooldownSeconds), cancellationToken);
			}
		}

		// Final token summary (always shown in verbose mode, also in normal mode if ShowProgress is enabled)
		if ((Verbose || (settings.ShowProgress ?? true)) && !settings.DryRun)
		{
			LogVerboseInfo("");
			LogVerboseInfo("=== FINAL TOKEN SUMMARY ===");
			LogVerboseInfo($"Total Input Tokens:     {_cumulativeInputTokens,6:N0}");
			LogVerboseInfo($"Total Output Tokens:    {_cumulativeOutputTokens,6:N0}");
			if (_cumulativeReasoningTokens > 0)
				LogVerboseInfo($"Total Reasoning Tokens: {_cumulativeReasoningTokens,6:N0}");
			LogVerboseInfo($"Total Tokens:           {_cumulativeTotalTokens,6:N0}");
			LogVerboseInfo("==========================");
		}
	}

	/// <summary>
	/// Parses a profile file (.yaml) containing settings and system prompt.
	/// </summary>
	private static async Task<Settings> ParseProfileFile(string profileFile)
	{
		if (!File.Exists(profileFile))
			throw new FileNotFoundException($"Profile file '{profileFile}' does not exist.");

		var content = await File.ReadAllTextAsync(profileFile).ConfigureAwait(false);

		var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
			.WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build();

		return deserializer.Deserialize<Settings>(content) ?? Settings.Empty;
	}

	public static Settings ParseCommandLine(string[] args)
	{
		static string MakeArgument(string value) => $"--{char.ToLowerInvariant(value[0])}{value[1..]}";

		var settings = new Settings();
		var providerOption = new Option<string>(MakeArgument(nameof(Settings.Provider)), "The provider name");
		var apiKeyOption = new Option<string>(MakeArgument(nameof(Settings.ApiKey)), "The API key used for authentication");
		var uriOption = new Option<string>(MakeArgument(nameof(Settings.Uri)), "The URI endpoint for the API");
		var modelOption = new Option<string>(MakeArgument(nameof(Settings.Model)), "The model name to be used");
		var targetPathOption = new Option<string>(MakeArgument(nameof(Settings.TargetPath)), "The source path where files are located");
		var profileOption = new Option<string>("--profile", "The path to the profile file (.yaml) containing settings and system prompt");
		var writeResponseToConsoleOption = new Option<bool>(MakeArgument(nameof(Settings.WriteResponseToConsole)), "Whether to write the model's response to the console");
		var applyCodeblockOption = new Option<bool?>(MakeArgument(nameof(Settings.ApplyCodeblock)), "Extract codeblock from response and overwrite file (false = output to console)");
		applyCodeblockOption.Arity = ArgumentArity.ZeroOrOne;
		var dryRunOption = new Option<bool>(MakeArgument(nameof(Settings.DryRun)), "Simulate processing without making actual changes");
		var assistantStarterOption = new Option<string>(MakeArgument(nameof(Settings.AssistantStarter)), "The string to start the assistant's message");
		var temperatureOption = new Option<float?>(MakeArgument(nameof(Settings.Temperature)), "The temperature value for the model");
		var systemPromptOption = new Option<string>(MakeArgument(nameof(Settings.SystemPrompt)), "The system prompt to be used with the model");
		var maxRetriesOption = new Option<int>(MakeArgument(nameof(Settings.MaxRetries)), "The maximum number of retries if a response could not be processed");
		var verboseOption = new Option<bool?>(MakeArgument(nameof(Settings.Verbose)), "Show detailed output including tokens and timing information");
		verboseOption.Arity = ArgumentArity.ZeroOrOne;
		var cooldownSecondsOption = new Option<int?>(MakeArgument(nameof(Settings.CooldownSeconds)), "Cooldown time in seconds after processing each file (prevents overheating)");
		var maxFileTokensOption = new Option<int?>(MakeArgument(nameof(Settings.MaxFileTokens)), "Maximum number of tokens a file may contain before it is skipped (default: 102400)");
		var resumeAtOption = new Option<string>(MakeArgument(nameof(Settings.ResumeAt)), "Resume processing from a specific file (skips all files until a filename containing this pattern is found)");
		var ollamaMinNumCtxOption = new Option<int?>(MakeArgument(nameof(Settings.OllamaMinNumCtx)), "Minimum context length for Ollama provider to prevent unnecessary model reloads (default: 20480)");
		var preserveWhitespaceOption = new Option<bool?>(MakeArgument(nameof(Settings.PreserveWhitespace)), "Preserve original leading and trailing whitespace to avoid diff noise (default: false)");
		preserveWhitespaceOption.Arity = ArgumentArity.ZeroOrOne;
		var showProgressOption = new Option<bool?>(MakeArgument(nameof(Settings.ShowProgress)), "Show progress indicator during file processing (default: true)");
		showProgressOption.Arity = ArgumentArity.ZeroOrOne;

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
			showProgressOption
		};

		rootCommand.SetHandler(context =>
		{
			settings.Provider = context.ParseResult.GetValueForOption(providerOption);
			settings.ApiKey = context.ParseResult.GetValueForOption(apiKeyOption);
			settings.Uri = context.ParseResult.GetValueForOption(uriOption) is string uri && !string.IsNullOrWhiteSpace(uri) ? new Uri(uri) : null;
			settings.Model = context.ParseResult.GetValueForOption(modelOption);
			settings.TargetPath = context.ParseResult.GetValueForOption(targetPathOption);
			settings.Profile = context.ParseResult.GetValueForOption(profileOption);
			settings.WriteResponseToConsole = context.ParseResult.GetValueForOption(writeResponseToConsoleOption);

			// Handle toggle flags: when specified without value, they default to true
			if (context.ParseResult.FindResultFor(applyCodeblockOption) is not null)
				settings.ApplyCodeblock = context.ParseResult.GetValueForOption(applyCodeblockOption) ?? true;

			settings.DryRun = context.ParseResult.GetValueForOption(dryRunOption);
			settings.AssistantStarter = context.ParseResult.GetValueForOption(assistantStarterOption);
			settings.Temperature = context.ParseResult.GetValueForOption(temperatureOption);
			settings.SystemPrompt = context.ParseResult.GetValueForOption(systemPromptOption);
			settings.MaxRetries = context.ParseResult.GetValueForOption(maxRetriesOption);

			// Handle toggle flags: when specified without value, they default to true
			if (context.ParseResult.FindResultFor(verboseOption) is not null)
				settings.Verbose = context.ParseResult.GetValueForOption(verboseOption) ?? true;

			settings.CooldownSeconds = context.ParseResult.GetValueForOption(cooldownSecondsOption);
			settings.MaxFileTokens = context.ParseResult.GetValueForOption(maxFileTokensOption);
			settings.ResumeAt = context.ParseResult.GetValueForOption(resumeAtOption);
			settings.OllamaMinNumCtx = context.ParseResult.GetValueForOption(ollamaMinNumCtxOption) ?? settings.OllamaMinNumCtx;

			// Handle toggle flags: when specified without value, they default to true
			if (context.ParseResult.FindResultFor(preserveWhitespaceOption) is not null)
				settings.PreserveWhitespace = context.ParseResult.GetValueForOption(preserveWhitespaceOption) ?? true;

			// Handle toggle flags: when specified without value, they default to true (but inverted: --showProgress false hides it)
			if (context.ParseResult.FindResultFor(showProgressOption) is not null)
				settings.ShowProgress = context.ParseResult.GetValueForOption(showProgressOption) ?? true;

			context.ExitCode = 0;
		});

		rootCommand.Invoke(args);

		return settings;
	}

	private static string GetFileSizeString(string file)
	{
		try
		{
			return $"{new FileInfo(file).Length / 1024.0:0.#} kB";
		}
		catch
		{
			return "??? kB";
		}
	}

	private static async Task<bool> ProcessFile(string file, string systemPromptTemplate, string retryMessage, Settings settings, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		string originalCode = string.Empty;
		Encoding originalEncoding;

		try
		{
			(originalCode, originalEncoding) = await ReadFileWithEncodingAsync(file, cancellationToken);
			LogVerboseDetail($"Detected encoding: {originalEncoding.EncodingName}");
		}
		catch (Exception ex)
		{
			LogError($"Could not read {file}: {ex.Message}");
			return false;
		}

		if (originalCode.Length < 1)
		{
			LogWarning($"Skipped file {file}: No content.");
			return false;
		}

		var fileTokens = CountTokens(originalCode);
		var maxTokens = settings.MaxFileTokens ?? 102400;
		if (fileTokens > maxTokens)
		{
			LogWarning($"Skipped file {file}: {fileTokens} tokens exceeds maximum of {maxTokens} tokens.");
			return false;
		}

		var codeLanguage = GetCodeLanguageByFileExtension(Path.GetExtension(file));

		var systemPrompt = systemPromptTemplate
			.Replace("%CODE%", originalCode)
			.Replace("%CODELANGUAGE%", codeLanguage)
			.Replace("%FILENAME%", Path.GetFileName(file));

		var userPrompt = """
%FILENAME%
``` %CODELANGUAGE%
%CODE%
```
""";
		userPrompt = userPrompt
			.Replace("%CODE%", originalCode)
			.Replace("%CODELANGUAGE%", codeLanguage)
			.Replace("%FILENAME%", Path.GetFileName(file));

		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, systemPrompt)
		};

		var systemPromptTokens = CountTokens(systemPrompt);
		var userPromptTokens = CountTokens(userPrompt);
		var inputTokens = systemPromptTokens + userPromptTokens;
		var estimatedResponseTokens = EstimateResponseTokens(settings, originalCode);
		var estimatedContextLength = EstimateContextLength(originalCode, systemPrompt, estimatedResponseTokens);

		var options = new ChatOptions { Temperature = settings.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, Math.Max(estimatedContextLength, settings.OllamaMinNumCtx)); // use a minimum context length for the Ollama provider to prevent unnecessary model reloads

		LogVerboseDetail($"Input tokens: {inputTokens} (system: {systemPromptTokens}, user: {userPromptTokens})");
		LogVerboseDetail($"Estimated output tokens: {estimatedResponseTokens}");
		LogVerboseDetail($"Estimated context length: {estimatedContextLength} tokens");

		var generatedCodeBuilder = new StringBuilder();

		var streamingUpdates = new List<ChatResponseUpdate>();

		var showProgress = settings.ShowProgress ?? true;

		if (showProgress)
		{
			await AnsiConsole.Progress()
				.AutoClear(true)
				.Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn(), new SpinnerColumn())
				.StartAsync(async ctx =>
				{
					var sendTask = ctx.AddTask("[green]Sending[/]");
					var waitTask = ctx.AddTask("[green]Waiting[/]");
					var receiveTask = ctx.AddTask("[green]Receiving[/]");

					while (!ctx.IsFinished)
					{
						sendTask.Increment(100);

						messages.Add(new ChatMessage(ChatRole.User, userPrompt));

						if (!string.IsNullOrEmpty(retryMessage))
							messages.Add(new ChatMessage(ChatRole.User, retryMessage));

						var hasAssistantStarter = !string.IsNullOrWhiteSpace(settings.AssistantStarter);
						if (hasAssistantStarter)
							messages.Add(new ChatMessage(ChatRole.Assistant, settings.AssistantStarter));

						await foreach (var update in ChatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
						{
							if (waitTask.Value == 0)
								waitTask.Increment(100);

							streamingUpdates.Add(update);
							generatedCodeBuilder.Append(update?.Text ?? "");
							receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, estimatedResponseTokens);
						}

						if (hasAssistantStarter)
							generatedCodeBuilder.Insert(0, settings.AssistantStarter);

						receiveTask.Value = 100;
					}
				});
		}
		else
		{
			// Process without progress indicator
			messages.Add(new ChatMessage(ChatRole.User, userPrompt));

			if (!string.IsNullOrEmpty(retryMessage))
				messages.Add(new ChatMessage(ChatRole.User, retryMessage));

			var hasAssistantStarter = !string.IsNullOrWhiteSpace(settings.AssistantStarter);
			if (hasAssistantStarter)
				messages.Add(new ChatMessage(ChatRole.Assistant, settings.AssistantStarter));

			await foreach (var update in ChatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
			{
				streamingUpdates.Add(update);
				generatedCodeBuilder.Append(update?.Text ?? "");
			}

			if (hasAssistantStarter)
				generatedCodeBuilder.Insert(0, settings.AssistantStarter);
		}

		// Convert streaming updates to ChatResponse to get usage information
		var response = streamingUpdates.ToChatResponse();

		// Show actual token usage from API response (more accurate) or fall back to local estimates
		var responseText = response?.Text ?? "";
		var apiUsage = response?.Usage;

		// Use API-reported values when available, otherwise fall back to local tokenizer estimates
		var actualInputTokens = (int)(apiUsage?.InputTokenCount ?? inputTokens);
		var actualOutputTokens = (int)(apiUsage?.OutputTokenCount ?? CountTokens(responseText));
		var actualTotalTokens = (int)(apiUsage?.TotalTokenCount ?? (actualInputTokens + actualOutputTokens));

		// Check for reasoning tokens in AdditionalCounts
		var reasoningTokens = 0L;
		if (apiUsage?.AdditionalCounts?.TryGetValue("reasoning_tokens", out reasoningTokens) != true)
			apiUsage?.AdditionalCounts?.TryGetValue("ReasoningTokens", out reasoningTokens);

		// Update cumulative counters
		_cumulativeInputTokens += actualInputTokens;
		_cumulativeOutputTokens += actualOutputTokens;
		_cumulativeReasoningTokens += reasoningTokens;
		_cumulativeTotalTokens += actualTotalTokens;

		var usageSource = apiUsage != null ? "API" : "estimated";
		LogVerboseDetail($"Output: {actualOutputTokens} tokens ({usageSource}, local estimate: {estimatedResponseTokens})");
		if (reasoningTokens > 0)
			LogVerboseDetail($"Reasoning tokens: {reasoningTokens}");
		LogVerboseDetail($"Total: {actualTotalTokens} tokens (input: {actualInputTokens} + output: {actualOutputTokens})");


		if (settings.WriteResponseToConsole ?? false)
		{
			LogVerboseInfo("");
			LogCode(responseText);
		}

		if (settings.ApplyCodeblock ?? true)
		{
			var extractedCode = CodeBlockExtractor.Extract(responseText);
			var couldExtractCode = !string.IsNullOrWhiteSpace(extractedCode);
			if (couldExtractCode)
			{
				string finalCode;
				if (settings.PreserveWhitespace ?? false)
				{
					// Preserve original leading and trailing whitespace to avoid diff noise
					var (leadingWhitespace, trailingWhitespace) = ExtractWhitespace(originalCode);
					finalCode = leadingWhitespace + extractedCode.Trim() + trailingWhitespace;
				}
				else
				{
					finalCode = extractedCode;
				}

				// Write with the same encoding as the original file (preserving BOM if present)
				await File.WriteAllTextAsync(file, finalCode, originalEncoding, cancellationToken);
				// Standard output: result in cyan
				LogResult($"{originalEncoding.GetByteCount(finalCode) + originalEncoding.GetPreamble().Length} bytes written");
			}
			else
			{
				var wasOkay = responseText.Trim().EndsWith("[OK]", StringComparison.OrdinalIgnoreCase) || responseText.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);
				if (wasOkay)
				{
					LogResult("no changes");
					return true;
				}

				LogError("Could not extract code from the model's response. It seems that there's no valid code block.");
				return false;
			}
		}
		else
		{
			// Standard output: response in cyan
			LogResult(responseText);
		}

		// Show per-file and cumulative token totals when Verbose or ShowProgress is enabled
		if ((Verbose || showProgress) && !settings.DryRun)
		{
			// Per-file tokens (6-digit fixed width for alignment)
			var fileReasoningPart = reasoningTokens > 0 ? $"   [dim]🧠 {reasoningTokens,6:N0}[/]" : "";
			AnsiConsole.MarkupLine($"[gray]File:    {stopwatch.Elapsed:hh':'mm':'ss}   [/][blue]↑ {actualOutputTokens,6:N0}[/]   [cyan]↓ {actualInputTokens,6:N0}[/]{fileReasoningPart}   [yellow]Σ {actualTotalTokens,6:N0}[/]");

			// Cumulative tokens (6-digit fixed width for alignment)
			var cumulativeReasoningPart = _cumulativeReasoningTokens > 0 ? $" [dim]🧠 {_cumulativeReasoningTokens,6:N0}[/]" : "";
			AnsiConsole.MarkupLine($"[gray]Total:   {_totalStopwatch.Elapsed:hh':'mm':'ss}   [/][blue]↑ {_cumulativeOutputTokens,6:N0}[/]   [cyan]↓ {_cumulativeInputTokens,6:N0}[/]{cumulativeReasoningPart}   [yellow]Σ {_cumulativeTotalTokens,6:N0}[/]");
		}

		return true;
	}

	private static IChatClient CreateChatClient(Settings settings)
	{
		var timeout = TimeSpan.FromMinutes(15);
		var provider = settings.Provider ?? string.Empty;

		if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
		{
			return new OllamaApiClient(new HttpClient { BaseAddress = settings.Uri, Timeout = timeout }, settings.Model ?? string.Empty);
		}
		else if (provider.Equals("lmstudio", StringComparison.OrdinalIgnoreCase) || provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
		{
			// LM Studio and other OpenAI-compatible servers
			// LM Studio default endpoint: http://localhost:1234/v1
			// API key can be empty or any string for local servers

			var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? "lm-studio" : settings.ApiKey;
			return new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = settings.Uri, NetworkTimeout = timeout }).GetChatClient(settings.Model ?? string.Empty).AsIChatClient();
		}
		else
		{
			// OpenAI

			return new OpenAI.OpenAIClient(new ApiKeyCredential(settings.ApiKey ?? string.Empty), new OpenAI.OpenAIClientOptions { Endpoint = settings.Uri, NetworkTimeout = timeout }).GetChatClient(settings.Model ?? string.Empty).AsIChatClient();
		}
	}

	/// <summary>
	/// Writes the file header (always visible, in cyan).
	/// </summary>
	private static void LogFileHeader(string message)
	{
		WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes the result output (always visible, in cyan).
	/// </summary>
	private static void LogResult(string message)
	{
		WriteLine("cyan", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes informational output only in verbose mode.
	/// </summary>
	private static void LogVerboseInfo(string message)
	{
		if (Verbose)
			WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes detailed output only in verbose mode.
	/// </summary>
	private static void LogVerboseDetail(string message)
	{
		if (Verbose)
			WriteLine("gray", message);
		Log.Verbose(message);
	}

	private static void LogWarning(string message)
	{
		WriteLine("yellow", message);
		Log.Warning(message);
	}

	private static void LogError(string message)
	{
		WriteLine("red", message);
		Log.Error(message);
	}

	private static void LogCode(string message)
	{
		WriteLine("cyan", message);
		Log.Debug(Environment.NewLine + message);
	}

	private static void WriteLine(string color, string message)
	{
		AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
	}

	private static int CalculateProgress(double generatedChars, double estimatedTokens)
	{
		// Approximate tokens from characters (average ~4 characters per token for code)

		var estimatedGeneratedTokens = generatedChars / 4.0;
		var percentage = 0.0d;

		if (estimatedTokens > 0 && estimatedGeneratedTokens > 0)
			percentage = (estimatedGeneratedTokens / estimatedTokens) * 100;

		return Math.Min(100, Math.Max(0, (int)percentage));
	}

	private static string GetCodeLanguageByFileExtension(string fileExtension)
	{
		return fileExtension.ToLower() switch
		{
			".cs" => "csharp",
			".js" => "javascript",
			".ts" => "typescript",
			".java" => "java",
			".py" => "python",
			".cpp" => "cpp",
			".c" => "c",
			".rb" => "ruby",
			".php" => "php",
			".html" => "html",
			".css" => "css",
			".xml" => "xml",
			".json" => "json",
			".sh" => "bash",
			".vb" => "vbnet",
			".md" => "markdown",
			".rs" => "rust",
			".go" => "go",
			".swift" => "swift",
			".kt" => "kotlin",
			".m" => "objectivec",
			".pl" => "perl",
			".r" => "r",
			".sql" => "sql",
			".scss" => "scss",
			".less" => "less",
			".scala" => "scala",
			".lua" => "lua",
			".dart" => "dart",
			".jsx" => "javascript",
			".tsx" => "typescript",
			".bat" => "bat",
			".cmd" => "bat",
			".ini" => "ini",
			".cfg" => "ini",
			".yaml" => "yaml",
			".yml" => "yaml",
			".h" => "c",
			".hpp" => "cpp",
			".coffee" => "coffeescript",
			".erl" => "erlang",
			".ex" => "elixir",
			".exs" => "elixir",
			".hs" => "haskell",
			".jl" => "julia",
			".ps1" => "powershell",
			".f90" => "fortran",
			".asm" => "nasm",
			".v" => "verilog",
			_ => ""
		};
	}


	/// <summary>
	/// Counts the actual number of tokens in a text using a real tokenizer.
	/// </summary>
	private static int CountTokens(string text)
	{
		if (string.IsNullOrEmpty(text))
			return 0;

		return Tokenizer.CountTokens(text);
	}

	private static int EstimateResponseTokens(Settings settings, string code)
	{
		var systemPromptTokens = CountTokens(settings.SystemPrompt ?? string.Empty);
		var codeTokens = CountTokens(code);

		if (settings.ApplyCodeblock ?? true)
			return (int)(systemPromptTokens + codeTokens * 1.25f);  // The LLM will extend/modify the code
		else
			return (int)(systemPromptTokens + codeTokens * 0.25f);  // Only short responses expected
	}

	private static int EstimateContextLength(string originalCode, string systemPrompt, int estimatedResponseTokens)
	{
		var systemPromptTokens = CountTokens(systemPrompt);
		var codeTokens = CountTokens(originalCode);
		var contextLength = systemPromptTokens + codeTokens + estimatedResponseTokens;
		return Math.Max(2048, contextLength);
	}

	/// <summary>
	/// Extracts the leading and trailing whitespace from a string.
	/// Used to preserve original file whitespace when replacing content.
	/// </summary>
	private static (string leading, string trailing) ExtractWhitespace(string text)
	{
		if (string.IsNullOrEmpty(text))
			return (string.Empty, string.Empty);

		var trimmed = text.Trim();
		if (trimmed.Length == 0)
			return (text, string.Empty); // All whitespace

		var leadingLength = text.IndexOf(trimmed[0]);
		var trailingStart = text.LastIndexOf(trimmed[^1]) + 1;

		var leading = text[..leadingLength];
		var trailing = text[trailingStart..];

		return (leading, trailing);
	}

	/// <summary>
	/// Reads a file and detects its encoding. Uses StreamReader for BOM detection
	/// and UTF-Unknown library as fallback for content-based detection.
	/// </summary>
	private static async Task<(string content, Encoding encoding)> ReadFileWithEncodingAsync(string file, CancellationToken cancellationToken)
	{
		// First, read the file bytes to check for BOM and use content-based detection
		var fileBytes = await File.ReadAllBytesAsync(file, cancellationToken);

		if (fileBytes.Length == 0)
			return (string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

		// Use UTF-Unknown for encoding detection (handles both BOM and content-based detection)
		var detectionResult = CharsetDetector.DetectFromBytes(fileBytes);

		Encoding encoding;
		if (detectionResult.Detected != null)
		{
			var detectedEncoding = detectionResult.Detected.Encoding;
			var hasBom = detectionResult.Detected.HasBOM;

			// ASCII is a subset of UTF-8, treat as UTF-8 without BOM
			// For UTF-8, preserve BOM status
			if (detectedEncoding.WebName.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
				encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			else if (detectedEncoding is UTF8Encoding || detectedEncoding.WebName.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
				encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);
			else
				encoding = detectedEncoding;
		}
		else
		{
			// Fallback to UTF-8 without BOM
			encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		}

		// Use StreamReader to properly decode the content (handles BOM automatically)
		using var stream = new MemoryStream(fileBytes);
		using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
		var content = await reader.ReadToEndAsync(cancellationToken);

		return (content, encoding);
	}
}