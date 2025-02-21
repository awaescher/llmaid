using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OllamaSharp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Serilog;
using Spectre.Console;
using OllamaSharp.Models;
using System.ClientModel;
using System.CommandLine;

namespace llmaid;

internal static class Program
{
	internal static IChatClient ChatClient { get; set; } = null!;
	internal static int FileCount { get; set; }
	internal static int CurrentFileIndex { get; set; }

	static async Task Main(string[] args)
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		var cancellationToken = cancellationTokenSource.Token;

		// settings 1: appsettings.json
		var config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
			.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
			.AddJsonFile($"appsettings.{(Debugger.IsAttached ? "VisualStudio" : "Production")}.json", optional: true)
			.Build();

		// settings 2: override values from command line
		// settings 3: override values from <OverrideSettings> in definition file
		var settings = config.Get<Settings>() ?? throw new ArgumentException("Settings could not be parsed.");
		settings.OverrideWith(ParseCommandLine(args));
		settings.OverrideWith(await ParseOverridesFromDefinitionFile(settings.DefinitionFile ?? string.Empty));

		await settings.Validate();

		Log.Logger = new LoggerConfiguration()
			.WriteTo.File($"./logs/{DateTime.Now:yyyy-MM-dd-hh-mm-ss}.log")
			.MinimumLevel.Verbose()
			.CreateLogger();

		ChatClient = CreateChatClient(settings);

		Information($"Running {settings.Model} ({settings.Uri}) against {settings.TargetPath}." + Environment.NewLine);
		Detail(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

		var loader = new FileLoader();
		var totalStopWatch = Stopwatch.StartNew();

		Information("Locating all files ..." + Environment.NewLine);
		var files = loader.GetAll(settings.TargetPath ?? string.Empty, settings.Files ?? new Files([], []));
		FileCount = files.Length;

		var errors = 0;
		foreach (var file in files)
		{
			Information($"[{++CurrentFileIndex}/{FileCount}] {file} ({GetFileSizeString(file)})");

			var success = false;
			var attempt = 0;
			do
			{
				attempt++;
				if (attempt > 1)
					Detail("Attempt " + attempt);
				var stopwatch = Stopwatch.StartNew();
				try
				{
					var retryMessage = attempt > 1 ? $"This is the {attempt}. attempt to process this file, all prior attempts failed because your response could not be processed. Please follow the instructions more closely." : "";
					if (settings.DryRun)
						success = true;
					else
						success = await ProcessFile(file, settings.SystemPrompt ?? string.Empty, retryMessage, settings, cancellationToken);
				}
				finally
				{
					stopwatch.Stop();
					if (!settings.DryRun)
						Detail($"{stopwatch.Elapsed}{Environment.NewLine}");
				}
			} while (!success && attempt < settings.MaxRetries);

			if (!success)
				errors++;
		}

		Information("Finished in " + totalStopWatch.Elapsed.ToString());
		Information($"{errors} error{(errors == 1 ? "" : "s")} occured.");
	}

	private static async Task<Settings> ParseOverridesFromDefinitionFile(string definitionFile)
	{
		const string XML_TAG = "OverrideSettings";

		var content = await File.ReadAllTextAsync(definitionFile).ConfigureAwait(false);

		var codeBlock = CodeBlockExtractor.ExtractXml(content, XML_TAG) ?? string.Empty;

		if (codeBlock.Trim().Any())
			return JsonSerializer.Deserialize<Settings>(codeBlock, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip }) ?? Settings.Empty;

		return Settings.Empty;
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
		var definitionFileOption = new Option<string>(MakeArgument(nameof(Settings.DefinitionFile)), "The path to the file defining the system prompt");
		var writeResponseToConsoleOption = new Option<bool>(MakeArgument(nameof(Settings.WriteResponseToConsole)), "Whether to write the model's response to the console");
		var modeOption = new Option<string>(MakeArgument(nameof(Settings.Mode)), "The mode in which llmaid is operating");
		var assistantStarterOption = new Option<string>(MakeArgument(nameof(Settings.AssistantStarter)), "The string to start the assistant's message");
		var temperatureOption = new Option<float?>(MakeArgument(nameof(Settings.Temperature)), "The temperature value for the model");
		var systemPromptOption = new Option<string>(MakeArgument(nameof(Settings.SystemPrompt)), "The system prompt to be used with the model");
		var maxRetriesOption = new Option<int>(MakeArgument(nameof(Settings.MaxRetries)), "The maximum number of retries if a response could not be processed");

		var rootCommand = new RootCommand
		{
			providerOption,
			apiKeyOption,
			uriOption,
			modelOption,
			targetPathOption,
			definitionFileOption,
			writeResponseToConsoleOption,
			modeOption,
			assistantStarterOption,
			temperatureOption,
			systemPromptOption,
			maxRetriesOption
		};

		rootCommand.SetHandler(context =>
		{
			settings.Provider = context.ParseResult.GetValueForOption(providerOption);
			settings.ApiKey = context.ParseResult.GetValueForOption(apiKeyOption);
			settings.Uri = context.ParseResult.GetValueForOption(uriOption) is string uri && !string.IsNullOrWhiteSpace(uri) ? new Uri(uri) : null;
			settings.Model = context.ParseResult.GetValueForOption(modelOption);
			settings.TargetPath = context.ParseResult.GetValueForOption(targetPathOption);
			settings.DefinitionFile = context.ParseResult.GetValueForOption(definitionFileOption);
			settings.WriteResponseToConsole = context.ParseResult.GetValueForOption(writeResponseToConsoleOption);
			settings.Mode = context.ParseResult.GetValueForOption(modeOption);
			settings.AssistantStarter = context.ParseResult.GetValueForOption(assistantStarterOption);
			settings.Temperature = context.ParseResult.GetValueForOption(temperatureOption);
			settings.SystemPrompt = context.ParseResult.GetValueForOption(systemPromptOption);
			settings.MaxRetries = context.ParseResult.GetValueForOption(maxRetriesOption);
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
		string originalCode = string.Empty;

		try
		{
			originalCode = await File.ReadAllTextAsync(file, cancellationToken);
		}
		catch (Exception ex)
		{
			Error($"Could not read {file}: {ex.Message}");
			return false;
		}

		if (originalCode.Length < 1)
		{
			Warning($"Skipped file {file}: No content.");
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
			new() { Role = ChatRole.System, Text = systemPrompt }
		};

		var estimatedResponseLength = EstimateResponseTokens(settings, originalCode);
		var estimatedContextLength = EstimateContextLength(originalCode, systemPrompt, estimatedResponseLength);

		var options = new ChatOptions { Temperature = settings.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, estimatedContextLength);

		Information($"Estimated context length for system prompt ({systemPrompt.Length} chars) and code ({originalCode.Length} chars): {estimatedContextLength} tokens");

		var generatedCodeBuilder = new StringBuilder();

		ChatResponseUpdate? response = null;

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

					messages.Add(new ChatMessage { Role = ChatRole.User, Text = userPrompt });

					if (!string.IsNullOrEmpty(retryMessage))
						messages.Add(new ChatMessage { Role = ChatRole.User, Text = retryMessage });

					var hasAssistantStarter = !string.IsNullOrWhiteSpace(settings.AssistantStarter);
					if (hasAssistantStarter)
						messages.Add(new ChatMessage { Role = ChatRole.Assistant, Text = settings.AssistantStarter });

					response = await ChatClient.GetStreamingResponseAsync(messages, options, cancellationToken).StreamToEndAsync(token =>
					{
						if (waitTask.Value == 0)
							waitTask.Increment(100);

						generatedCodeBuilder.Append(token?.Text ?? "");
						receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, estimatedResponseLength);
					}).ConfigureAwait(false);

					if (hasAssistantStarter && response != null)
						response.Text = settings.AssistantStarter + response.Text;

					receiveTask.Value = 100;
				}
			});

		if (settings.WriteResponseToConsole ?? false)
		{
			Information("");
			Code(response?.Text ?? "");
		}

		if (settings.IsReplaceMode)
		{
			var extractedCode = CodeBlockExtractor.Extract(response?.Text ?? "");
			var couldExtractCode = !string.IsNullOrWhiteSpace(extractedCode);
			if (couldExtractCode)
			{
				await File.WriteAllTextAsync(file, extractedCode, cancellationToken);
			}
			else
			{
				var wasOkay = (response?.Text ?? "").Trim().EndsWith("[OK]", StringComparison.OrdinalIgnoreCase);
				if (wasOkay)
				{
					Information("The model returned 'OK' signaling that no changes were required.");
					return true;
				}

				Error("Could not extract code from the model's response. It seems that there's no valid code block.");
				return false;
			}
		}

		if (settings.IsFindMode && !(settings.WriteResponseToConsole ?? false))
			Code(response?.Text ?? "");

		return true;
	}

	private static IChatClient CreateChatClient(Settings settings)
	{
		var timeout = TimeSpan.FromMinutes(15);

		if ((settings.Provider ?? string.Empty).Equals("ollama", StringComparison.OrdinalIgnoreCase))
			return new OllamaApiClient(new HttpClient { BaseAddress = settings.Uri, Timeout = timeout }, settings.Model ?? string.Empty);
		else
			return new OpenAIChatClient(new OpenAI.OpenAIClient(new ApiKeyCredential(settings.ApiKey ?? string.Empty), new OpenAI.OpenAIClientOptions { Endpoint = settings.Uri, NetworkTimeout = timeout }), settings.Model ?? string.Empty);
	}

	private static void Information(string message)
	{
		WriteLine("white", message);
		Log.Information(message);
	}

	private static void Warning(string message)
	{
		WriteLine("yellow", message);
		Log.Warning(message);
	}

	private static void Error(string message)
	{
		WriteLine("red", message);
		Log.Error(message);
	}

	private static void Code(string message)
	{
		WriteLine("cyan", message);
		Log.Debug(Environment.NewLine + message);
	}

	private static void Detail(string message)
	{
		WriteLine("gray", message);
		Log.Verbose(message);
	}

	private static void WriteLine(string color, string message)
	{
		AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
	}

	private static int CalculateProgress(double generatedCodeLength, double originalCodeLength)
	{
		var percentage = 0.0d;

		if (originalCodeLength > 0 && generatedCodeLength > 0)
			percentage = (generatedCodeLength / originalCodeLength) * 100;

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

	private static int EstimateResponseTokens(Settings settings, string code)
	{
		var inputLength = (settings.SystemPrompt ?? string.Empty).Length + code.Length;

		if (settings.IsReplaceMode)
			return (int)((inputLength + 1.25f * code.Length) / 3);  // fake the estimated length, the LLM is going to extend the class

		if (settings.IsFindMode)
			return (int)((inputLength + 0.25f * code.Length) / 3);

		throw new NotSupportedException();
	}

	private static int EstimateContextLength(string originalCode, string systemPrompt, int estimatedResponseTokens)
	{
		var contextLength = (systemPrompt.Length + originalCode.Length + estimatedResponseTokens) / 3;
		return Math.Max(2048, contextLength);
	}
}