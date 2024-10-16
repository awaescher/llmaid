using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using Spectre.Console;

namespace llmaid;

internal static class Program
{
	static async Task Main(string[] args)
	{
		using var cancellationTokenSource = new CancellationTokenSource();
		var cancellationToken = cancellationTokenSource.Token;

		var config = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
			.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
			.Build();

		var arguments = config.GetSection("Arguments").Get<Arguments>() ?? throw new ArgumentException("Arguments could not be parsed.");
		arguments.Validate();

		var systemPromptTemplate = await File.ReadAllTextAsync(arguments.PromptFile, cancellationToken);

		var ollama = new OllamaApiClient(arguments.Uri, arguments.Model);
		var loader = new FileLoader();
		var totalStopWatch = Stopwatch.StartNew();

		foreach (var file in loader.Get(arguments.SourcePath, arguments.FilePatterns))
		{
			var originalCode = await File.ReadAllTextAsync(file, cancellationToken);

			if (originalCode.Length < 1)
			{
				Warning($"Skipped file {file}: No content.");
				continue;
			}
			Information($"{file} ({originalCode.Length} char{(originalCode.Length == 1 ? "" : "s")})");

			var systemPrompt = systemPromptTemplate
				.Replace("%CODE%", originalCode)
				.Replace("%CODELANGUAGE%", GetCodeLanguageByFileExtension(Path.GetExtension(file)))
				.Replace("%FILENAME%", Path.GetFileName(file));

			var userPrompt = """
%FILENAME%
``` %CODELANGUAGE%
%CODE%
```
""";
			userPrompt = userPrompt
				.Replace("%CODE%", originalCode)
				.Replace("%CODELANGUAGE%", GetCodeLanguageByFileExtension(Path.GetExtension(file)))
				.Replace("%FILENAME%", Path.GetFileName(file));

			var chat = new Chat(ollama);
			chat.Messages.Add(new Message { Role = ChatRole.System, Content = systemPrompt });

			if (arguments.Temperature.HasValue)
				chat.Options = new OllamaSharp.Models.RequestOptions { Temperature = arguments.Temperature };

			var generatedCodeBuilder = new StringBuilder();

			var response = "";

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

						response = await chat.Send(userPrompt, cancellationToken).StreamToEnd(token =>
						{
							if (waitTask.Value == 0)
								waitTask.Increment(100);

							generatedCodeBuilder.Append(token);
							receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, originalCode.Length * 1.20);  // fake the estimated length, the LLM is going to extend the class
						});

						receiveTask.Value = 100;
					}
				});

			stopwatch.Stop();

			if (arguments.WriteCodeToConsole)
			{
				Information("");
				Code(response);
			}

			var extractedCode = CodeBlockExtractor.Extract(response);
			var couldExtractCode = !string.IsNullOrWhiteSpace(extractedCode);
			if (!couldExtractCode)
				Error("Could not extract code from the model's response. It seems that there's no valid code block.");

			if (!arguments.DryRun && couldExtractCode)
				await File.WriteAllTextAsync(file, extractedCode, cancellationToken);

			Detail($"{stopwatch.Elapsed}{Environment.NewLine}");
		}

		Information("Finished in " + totalStopWatch.Elapsed.ToString());
	}

	private static void WriteLine(string color, string message)
	{
		AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
	}

	private static void Information(string message) => WriteLine("white", message);

	private static void Warning(string message) => WriteLine("yellow", message);

	private static void Error(string message) => WriteLine("red", message);

	private static void Code(string message) => WriteLine("cyan", message);

	private static void Detail(string message) => WriteLine("gray", message);

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