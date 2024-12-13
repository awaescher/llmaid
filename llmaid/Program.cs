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

		if (args.Length != 2)
			throw new ArgumentException("llmaid requires two arguments: the definition file and a target path.");

		var definitionFile = args[0];
		if (string.IsNullOrEmpty(definitionFile))
			throw new ArgumentException("Missing definition file");
		if (!File.Exists(definitionFile))
			throw new ArgumentException("Definition file does not exist:" + definitionFile);

		var targetPath = args[1];
		if (string.IsNullOrEmpty(targetPath))
			throw new ArgumentException("Missing target path");
		if (!Directory.Exists(targetPath))
			throw new ArgumentException("Target path does not exist: " + targetPath);

		var config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
			.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
			.AddJsonFile($"appsettings.{(Debugger.IsAttached ? "VisualStudio" : "Production")}.json", optional: true)
			.Build();

		Log.Logger = new LoggerConfiguration()
			.WriteTo.File($"./logs/{DateTime.Now:yyyy-MM-dd-hh-mm-ss}.log")
			.MinimumLevel.Verbose()
			.CreateLogger();

		var arguments = config.Get<Arguments>() ?? throw new ArgumentException("Arguments could not be parsed.");
		arguments.DefinitionFile = definitionFile;
		arguments.TargetPath = targetPath;
		await arguments.Validate();

		ChatClient = CreateChatClient(arguments);

		Information($"Running {arguments.Model} ({arguments.Uri}) against {arguments.TargetPath}." + Environment.NewLine);
		Log.Debug(JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true }));

		var loader = new FileLoader();
		var totalStopWatch = Stopwatch.StartNew();

		Information("Locating all files ..." + Environment.NewLine);
		var files = loader.GetAll(arguments.TargetPath, arguments.Files);
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
					success = await ProcessFile(file, arguments.SystemPrompt, retryMessage, arguments, cancellationToken);
				}
				finally
				{
					stopwatch.Stop();
					Detail($"{stopwatch.Elapsed}{Environment.NewLine}");
				}
			} while (!success && attempt < arguments.MaxRetries);

			if (!success)
				errors++;
		}

		Information("Finished in " + totalStopWatch.Elapsed.ToString());
		Information($"{errors} error{(errors == 1 ? "" : "s")} occured.");
	}

	private static object GetFileSizeString(string file)
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

	private static async Task<bool> ProcessFile(string file, string systemPromptTemplate, string retryMessage, Arguments arguments, CancellationToken cancellationToken)
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

		var estimatedResponseLength = ResponseEstimator.EstimateResponseTokens(arguments, originalCode);
		var estimatedContextLength = (int)((systemPrompt.Length + originalCode.Length + estimatedResponseLength) / 3);
		var options = new ChatOptions { Temperature = arguments.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, estimatedContextLength);

		Log.Logger.Debug($"Estimated context length for system prompt ({systemPrompt.Length} chars) and code ({originalCode.Length} chars): {estimatedContextLength} tokens");
		Information($"Estimated context length for system prompt ({systemPrompt.Length} chars) and code ({originalCode.Length} chars): {estimatedContextLength} tokens");

		var generatedCodeBuilder = new StringBuilder();

		StreamingChatCompletionUpdate? response = null;

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

					var hasAssistantStarter = !string.IsNullOrWhiteSpace(arguments.AssistantStarter);
					if (hasAssistantStarter)
						messages.Add(new ChatMessage { Role = ChatRole.Assistant, Text = arguments.AssistantStarter });

					response = await ChatClient.CompleteStreamingAsync(messages, options, cancellationToken).StreamToEndAsync(token =>
					{
						if (waitTask.Value == 0)
							waitTask.Increment(100);

						generatedCodeBuilder.Append(token?.Text ?? "");
						receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, estimatedResponseLength);
					}).ConfigureAwait(false);

					if (hasAssistantStarter && response != null)
						response.Text = arguments.AssistantStarter + response.Text;

					receiveTask.Value = 100;
				}
			});

		if (arguments.WriteResponseToConsole)
		{
			Information("");
			Code(response?.Text ?? "");
		}

		if (arguments.IsReplaceMode)
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

		if (arguments.IsFindMode && !arguments.WriteResponseToConsole)
			Code(response?.Text ?? "");

		return true;
	}

	private static IChatClient CreateChatClient(Arguments arguments)
	{
		var timeout = TimeSpan.FromMinutes(15);

		if (arguments.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
			return new OllamaApiClient(new HttpClient { BaseAddress = arguments.Uri, Timeout = timeout }, arguments.Model);
		else
			return new OpenAIChatClient(new OpenAI.OpenAIClient(new ApiKeyCredential(arguments.ApiKey), new OpenAI.OpenAIClientOptions { Endpoint = arguments.Uri, NetworkTimeout = timeout }), arguments.Model);
	}

	private static void WriteLine(string color, string message)
	{
		AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
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

	internal class ResponseEstimator
	{
		public static float EstimateResponseTokens(Arguments arguments, string code)
		{
			var inputLength = arguments.SystemPrompt.Length + code.Length;

			if (arguments.IsReplaceMode)
				return (int)((inputLength + 1.25f * code.Length) / 3);  // fake the estimated length, the LLM is going to extend the class

			if (arguments.IsFindMode)
				return (int)((inputLength + 0.25f * code.Length) / 3);

			throw new NotSupportedException();
		}
	}
}