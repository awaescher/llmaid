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
		arguments.Validate();

		var systemPromptTemplate = await File.ReadAllTextAsync(arguments.PromptFile, cancellationToken);

		ChatClient = CreateChatClient(arguments);

		Information($"Running {arguments.Model} ({arguments.Uri}) against {arguments.SourcePath}." + Environment.NewLine);
		Log.Debug(JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = true }));
		Log.Debug(systemPromptTemplate);

		var loader = new FileLoader();
		var totalStopWatch = Stopwatch.StartNew();

		Information($"Locating all files ..." + Environment.NewLine);
		var files = loader.GetAll(arguments.SourcePath, arguments.FilePatterns);
		FileCount = files.Length;

		var errors = 0;
		foreach (var file in files)
		{
			var success = await ProcessFile(file, systemPromptTemplate, arguments, cancellationToken);
			if (!success)
				errors++;
		}

		Information("Finished in " + totalStopWatch.Elapsed.ToString());
		Information($"{errors} error{(errors == 1 ? "" : "s")} occured.");
	}

	private static async Task<bool> ProcessFile(string file, string systemPromptTemplate, Arguments arguments, CancellationToken cancellationToken)
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

		Information($"[{++CurrentFileIndex}/{FileCount}] {file} ({originalCode.Length} char{(originalCode.Length == 1 ? "" : "s")})");

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
			new() { Role = ChatRole.Assistant, Text = systemPrompt }
		};

		var options = new ChatOptions { Temperature = arguments.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, originalCode.Length);

		var generatedCodeBuilder = new StringBuilder();

		StreamingChatCompletionUpdate? response = null;

		var stopwatch = Stopwatch.StartNew();

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

					response = await ChatClient.CompleteStreamingAsync(messages, options, cancellationToken).StreamToEndAsync(token =>
					{
						if (waitTask.Value == 0)
							waitTask.Increment(100);

						generatedCodeBuilder.Append(token?.Text ?? "");
						receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, originalCode.Length * 1.20);  // fake the estimated length, the LLM is going to extend the class
					}).ConfigureAwait(false);

					receiveTask.Value = 100;
				}
			});

		stopwatch.Stop();

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
				Error("Could not extract code from the model's response. It seems that there's no valid code block.");
				return false;
			}
		}

		if (arguments.IsFindMode && !arguments.WriteResponseToConsole)
			Code(response?.Text ?? "");

		Detail($"{stopwatch.Elapsed}{Environment.NewLine}");
		return true;
	}

	private static IChatClient CreateChatClient(Arguments arguments)
	{
		if (arguments.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
			return new OllamaApiClient(arguments.Uri, arguments.Model);
		else
			return new OpenAIChatClient(new OpenAI.OpenAIClient(new ApiKeyCredential(arguments.ApiKey), new OpenAI.OpenAIClientOptions { Endpoint = arguments.Uri }), arguments.Model);
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
}