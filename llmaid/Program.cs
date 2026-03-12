using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace llmaid;

internal static class Program
{
	static async Task Main(string[] args)
	{
		args ??= [];

		// Show help when no arguments are provided at all
		if (args.Length == 0)
			args = ["--help"];

		// Let --help and --version be handled by System.CommandLine without further processing
		if (args.Any(a => a is "-?" or "-h" or "--help" or "--version"))
		{
			CommandLineParser.Parse(args);
			Console.WriteLine();
			Console.WriteLine("  More information and usage examples:");
			Console.WriteLine("    https://github.com/awaescher/llmaid");
			Console.WriteLine();
			Console.WriteLine("  Profile examples:");
			Console.WriteLine("    https://github.com/awaescher/llmaid/tree/master/profiles");
			Console.WriteLine();
			return;
		}

		using var cancellationTokenSource = new CancellationTokenSource();
		var cancellationToken = cancellationTokenSource.Token;

		Settings settings;
		try
		{
			settings = await LoadSettings(args);
		}
		catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"Error: {ex.Message}");
			Console.ResetColor();
			Console.Error.WriteLine();
			Console.Error.WriteLine("Run with --help to see all available options.");
			Environment.Exit(1);
			return;
		}

		ConsoleLogger.Verbose = settings.Verbose ?? false;

		InitializeLogging();

		var chatClient = ChatClientFactory.Create(settings);

		LogStartupInfo(settings);

		var files = DiscoverFiles(settings);
		var processor = new FileProcessor(chatClient, settings, Stopwatch.StartNew());

		await ProcessAllFiles(files, settings, processor, cancellationToken);
	}

	/// <summary>
	/// Loads settings from three layers (each overriding the previous):
	/// 1. appsettings.json — base configuration
	/// 2. Profile file (.yaml) — task-specific settings and system prompt
	/// 3. Command line arguments — runtime overrides (highest priority)
	/// </summary>
	private static async Task<Settings> LoadSettings(string[] args)
	{
		// For single-file deployments (e.g. Homebrew), AppContext.BaseDirectory points to a temp extraction
		// directory, not the binary location. Environment.ProcessPath gives the actual executable path.
		var executableDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
		var config = new ConfigurationBuilder()
			.SetBasePath(executableDir)
			.AddJsonFile(Path.Combine(executableDir, "appsettings.json"), optional: true, reloadOnChange: false)
			.AddJsonFile(Path.Combine(executableDir, $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json"), optional: true, reloadOnChange: false)
			.AddJsonFile(Path.Combine(executableDir, $"appsettings.{(Debugger.IsAttached ? "VisualStudio" : "Production")}.json"), optional: true, reloadOnChange: false)
			.Build();

		var settings = config.Get<Settings>() ?? Settings.Empty;

		var cliSettings = CommandLineParser.Parse(args);
		var profilePath = cliSettings.Profile ?? settings.Profile;

		if (!string.IsNullOrWhiteSpace(profilePath))
			settings.OverrideWith(await ProfileParser.ParseAsync(profilePath));

		settings.OverrideWith(cliSettings);

		await settings.Validate(requireProfile: false);

		return settings;
	}

	private static void InitializeLogging()
	{
		Log.Logger = new LoggerConfiguration()
			.WriteTo.File($"./logs/{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log")
			.MinimumLevel.Verbose()
			.CreateLogger();
	}

	private static void LogStartupInfo(Settings settings)
	{
		ConsoleLogger.LogVerboseInfo($"Running {settings.Model} ({settings.Uri}) against {settings.TargetPath}." + Environment.NewLine);
		ConsoleLogger.LogVerboseDetail(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
	}

	private static string[] DiscoverFiles(Settings settings)
	{
		var loader = new FileLoader();

		ConsoleLogger.LogVerboseInfo("Locating all files ..." + Environment.NewLine);
		return loader.GetAll(settings.TargetPath ?? string.Empty, settings.Files ?? new Files([], []));
	}

	private static async Task ProcessAllFiles(string[] files, Settings settings, FileProcessor processor, CancellationToken cancellationToken)
	{
		var fileCount = files.Length;
		var errors = 0;
		var hasResumed = string.IsNullOrWhiteSpace(settings.ResumeAt);

		for (var i = 0; i < fileCount; i++)
		{
			var file = files[i];
			var fileIndex = i + 1;

			if (!hasResumed)
			{
				if (file.Contains(settings.ResumeAt!, StringComparison.OrdinalIgnoreCase))
				{
					hasResumed = true;
				}
				else
				{
					ConsoleLogger.LogVerboseDetail($"[{fileIndex}/{fileCount}] Skipping (resume): {file}");
					continue;
				}
			}

			ConsoleLogger.LogFileHeader($"[{fileIndex}/{fileCount}] {file} ({FileHelper.GetFileSizeString(file)})");

			var success = await ProcessFileWithRetries(file, settings, processor, cancellationToken);

			if (!success)
				errors++;

			await ApplyCooldown(settings, cancellationToken);
		}
	}

	private static async Task<bool> ProcessFileWithRetries(string file, Settings settings, FileProcessor processor, CancellationToken cancellationToken)
	{
		var attempt = 0;
		var success = false;

		do
		{
			attempt++;

			if (attempt > 1)
				ConsoleLogger.LogVerboseDetail("Attempt " + attempt);

			var retryMessage = attempt > 1
				? $"This is the {attempt}. attempt to process this file, all prior attempts failed because your response could not be processed. Please follow the instructions more closely."
				: "";

			if (settings.DryRun)
				success = true;
			else
				success = await processor.ProcessAsync(file, retryMessage, cancellationToken);

		} while (!success && attempt < settings.MaxRetries);

		return success;
	}

	private static async Task ApplyCooldown(Settings settings, CancellationToken cancellationToken)
	{
		var cooldownSeconds = settings.CooldownSeconds ?? 0;

		if (cooldownSeconds > 0 && !settings.DryRun)
		{
			ConsoleLogger.LogVerboseDetail($"Cooling down for {cooldownSeconds} seconds...");
			await Task.Delay(TimeSpan.FromSeconds(cooldownSeconds), cancellationToken);
		}
	}
}
