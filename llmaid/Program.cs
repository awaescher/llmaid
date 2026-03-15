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

		ConsoleLogger.Diagnostic = settings.Diagnostic ?? false;
		ConsoleLogger.Verbose = (settings.Verbose ?? false) || ConsoleLogger.Diagnostic;

		InitializeLogging();

		var chatClient = ChatClientFactory.Create(settings);

		LogStartupInfo(settings);

		var files = DiscoverFiles(settings);
		var processor = new FileProcessor(chatClient, settings, Stopwatch.StartNew());

		var judgeEnabled = (settings.JudgeMaxRetries ?? 0) > 0;
		var judgeMode = (settings.JudgeMode ?? "response").Trim().ToLowerInvariant();
		var useGitDiffJudge = judgeEnabled && (judgeMode is "git-diff" or "both");
		var applyCodeblock = settings.ApplyCodeblock ?? true;

		if (judgeMode is not ("response" or "git-diff" or "both"))
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.WriteLine($"Error: Unknown judgeMode '{judgeMode}'. Valid values are: 'response', 'git-diff', 'both'.");
			Console.ResetColor();
			Environment.Exit(1);
			return;
		}

		JudgeProcessor? judge = null;

		if (judgeEnabled)
		{
			// Validate git-diff mode preconditions at startup
			if (useGitDiffJudge)
			{
				if (!applyCodeblock)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.Error.WriteLine($"Warning: judgeMode '{judgeMode}' has no effect when applyCodeblock is false.");
					Console.Error.WriteLine("         No file is written, so no git diff is produced.");
					Console.Error.WriteLine("         Switch to judgeMode 'response' or enable applyCodeblock: true.");
					Console.ResetColor();
				}
				else if (!await GitHelper.IsInsideGitRepoAsync(settings.TargetPath!))
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Error.WriteLine($"Error: judgeMode '{judgeMode}' requires the target path to be inside a git repository.");
					Console.Error.WriteLine($"       Either switch to judgeMode 'response' (works without git), or run llmaid against files inside a git repository.");
					Console.ResetColor();
					Environment.Exit(1);
					return;
				}
			}

			var judgeClient = ChatClientFactory.CreateJudgeClient(settings);
			judge = new JudgeProcessor(judgeClient, settings);
		}

		await ProcessAllFiles(files, settings, processor, judge, judgeMode, cancellationToken);
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

	private static async Task ProcessAllFiles(string[] files, Settings settings, FileProcessor processor, JudgeProcessor? judge, string judgeMode, CancellationToken cancellationToken)
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

			var success = await ProcessFileWithJudge(file, settings, processor, judge, judgeMode, cancellationToken);

			if (!success)
				errors++;

			await ApplyCooldown(settings, cancellationToken);
		}
	}

	/// <summary>
	/// Outer judge loop. Orchestrates the two-phase flow:
	/// <list type="bullet">
	///   <item><term>response mode</term><description>After streaming, the response-judge evaluates the raw LLM output before writing.</description></item>
	///   <item><term>git-diff mode</term><description>After writing, the git-diff judge evaluates the actual diff.</description></item>
	///   <item><term>both</term><description>Response-judge runs first (pre-write); git-diff judge runs after writing.</description></item>
	/// </list>
	/// When no judge is configured the method delegates directly to
	/// <see cref="ProcessFileWithRetries"/> without any overhead.
	/// </summary>
	private static async Task<bool> ProcessFileWithJudge(string file, Settings settings, FileProcessor processor, JudgeProcessor? judge, string judgeMode, CancellationToken cancellationToken)
	{
		var judgeMaxRetries = settings.JudgeMaxRetries ?? 0;
		var applyCodeblock = settings.ApplyCodeblock ?? true;

		// No judge configured — stream and write in one shot
		if (judge == null || judgeMaxRetries <= 0 || settings.DryRun)
		{
			var noJudgeResult = await ProcessFileWithRetries(file, settings, processor, string.Empty, cancellationToken);
			if (!noJudgeResult.Success)
				return false;

			return await processor.WriteResponseAsync(file, noJudgeResult, cancellationToken);
		}

		var useResponseJudge = judgeMode is "response" or "both";
		var useGitDiffJudge = judgeMode is "git-diff" or "both";

		// git-diff judge requires file changes to be written and applyCodeblock=true
		var gitDiffJudgeActive = useGitDiffJudge && applyCodeblock;

		// Remember original content for git-diff mode (to restore on rejection)
		string? originalContent = null;
		if (gitDiffJudgeActive)
		{
			try
			{
				originalContent = await File.ReadAllTextAsync(file, cancellationToken);
			}
			catch (Exception ex)
			{
				// Binary/unreadable files — skip the git-diff judge, run response-only
				ConsoleLogger.LogVerboseDetail($"Judge: cannot read '{Path.GetFileName(file)}' as text ({ex.GetType().Name}), git-diff judge skipped.");
				gitDiffJudgeActive = false;
			}
		}

		var judgeRetryMessage = string.Empty;

		for (var judgeAttempt = 1; judgeAttempt <= judgeMaxRetries; judgeAttempt++)
		{
			// ── Phase 1: Stream LLM response (no write yet) ──────────────────
			var result = await ProcessFileWithRetries(file, settings, processor, judgeRetryMessage, cancellationToken);
			if (!result.Success)
				return false;

			// ── Phase 2: Response-judge (pre-write) ──────────────────────────
			if (useResponseJudge && !result.IsImage && result.OriginalCode != null)
			{
				var responseJudgeStopwatch = Stopwatch.StartNew();
				var responseVerdict = await judge.EvaluateResponseAsync(
					result.OriginalCode,
					result.ResponseText,
					settings.SystemPrompt ?? string.Empty,
					cancellationToken);
				responseJudgeStopwatch.Stop();

				processor.AccumulateJudgeTokens(responseVerdict.Usage, responseJudgeStopwatch);

				if (!responseVerdict.Passed)
				{
					var isLastAttempt = judgeAttempt >= judgeMaxRetries;
					var failHeader = !isLastAttempt
						? $"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✗ FAIL — retrying without writing"
						: $"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✗ FAIL — maximum review cycles reached, file not written";

					ConsoleLogger.LogWarning(failHeader);
					foreach (var violation in responseVerdict.Violations)
						ConsoleLogger.LogWarning($"  • {violation}");

					if (!isLastAttempt)
					{
						var violationList = string.Join(Environment.NewLine, responseVerdict.Violations.Select(v => $"  - {v}"));
						judgeRetryMessage = $"""
							A judge has reviewed your response and found the following violations of the task instructions:

							{violationList}

							Please redo the task, specifically addressing each violation listed above. Do NOT repeat these mistakes.
							""";
					}

					continue; // retry — do not write
				}

				ConsoleLogger.LogResult($"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✓ PASS");
			}
	
			// ── Phase 3: Write response to disk (or console) ─────────────────
			var writeSuccess = await processor.WriteResponseAsync(file, result, cancellationToken);
			if (!writeSuccess)
				return false;
	
			// ── Phase 4: Git-diff judge (post-write) ─────────────────────────
			if (!gitDiffJudgeActive)
				return true;
	
			var diff = await GitHelper.GetDiffAsync(file);

			if (string.IsNullOrWhiteSpace(diff))
			{
				// No changes were made — treat as pass (LLM said OK, nothing to judge)
				ConsoleLogger.LogVerboseDetail("Judge: no diff detected, skipping git-diff review.");
				return true;
			}

			var diffJudgeStopwatch = Stopwatch.StartNew();
			var diffVerdict = await judge.EvaluateAsync(diff, settings.SystemPrompt ?? string.Empty, cancellationToken);
			diffJudgeStopwatch.Stop();

			processor.AccumulateJudgeTokens(diffVerdict.Usage, diffJudgeStopwatch);

			if (diffVerdict.Passed)
			{
				ConsoleLogger.LogResult($"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✓ PASS");
				return true;
			}
	
			// Diff-judge rejected — log violations and restore the file
			var diffIsLastAttempt = judgeAttempt >= judgeMaxRetries;
			var diffFailHeader = !diffIsLastAttempt
				? $"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✗ FAIL — restoring file and retrying"
				: $"Judge [{judgeAttempt}/{judgeMaxRetries}]: ✗ FAIL — maximum review cycles reached, restoring original file";

			ConsoleLogger.LogWarning(diffFailHeader);
			foreach (var violation in diffVerdict.Violations)
				ConsoleLogger.LogWarning($"  • {violation}");

			// Restore original content so the next attempt starts clean
			if (originalContent != null)
				await File.WriteAllTextAsync(file, originalContent, cancellationToken);

			if (!diffIsLastAttempt)
			{
				var violationList = string.Join(Environment.NewLine, diffVerdict.Violations.Select(v => $"  - {v}"));
				judgeRetryMessage = $"""
					A judge has reviewed the changes you made and found the following violations of the task instructions:

					{violationList}

					Please redo the task, specifically addressing each violation listed above. Do NOT repeat these mistakes.
					""";
			}
		}

		ConsoleLogger.LogWarning($"Judge: all {judgeMaxRetries} review cycle(s) exhausted — skipping file");
		return false;
	}

	private static async Task<ProcessResult> ProcessFileWithRetries(string file, Settings settings, FileProcessor processor, string judgeRetryMessage, CancellationToken cancellationToken)
	{
		var attempt = 0;
		ProcessResult result = new(false, string.Empty, null, null, false);

		do
		{
			attempt++;

			if (attempt > 1)
				ConsoleLogger.LogVerboseDetail("Attempt " + attempt);

			var internalRetryNotice = attempt > 1
				? $"This is the {attempt}. attempt to process this file, all prior attempts failed because your response could not be processed. Please follow the instructions more closely."
				: string.Empty;

			var retryParts = new[] { judgeRetryMessage, internalRetryNotice }.Where(s => !string.IsNullOrEmpty(s));
			var retryMessage = string.Join(Environment.NewLine + Environment.NewLine, retryParts);

			if (settings.DryRun)
				result = new ProcessResult(true, string.Empty, null, null, false);
			else
				result = await processor.ProcessAsync(file, retryMessage, cancellationToken);

		} while (!result.Success && attempt < settings.MaxRetries);

		return result;
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
