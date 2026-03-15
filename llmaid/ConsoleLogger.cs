using Serilog;
using Spectre.Console;

namespace llmaid;

/// <summary>
/// Centralized console and file logging with color-coded output levels.
/// </summary>
internal static class ConsoleLogger
{
	internal static bool Verbose { get; set; }

	/// <summary>
	/// When true, the full system prompt, user message, and LLM response are printed
	/// for every LLM call. Implies <see cref="Verbose"/>.
	/// </summary>
	internal static bool Diagnostic { get; set; }

	/// <summary>
	/// Writes a file header message to the console and log.
	/// </summary>
	internal static void LogFileHeader(string message)
	{
		WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes a result message to the console and log.
	/// </summary>
	internal static void LogResult(string message)
	{
		WriteLine("cyan", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes informational output to the console and log when verbose mode is enabled.
	/// </summary>
	internal static void LogVerboseInfo(string message)
	{
		if (Verbose)
			WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes detailed output to the console and log when verbose mode is enabled.
	/// </summary>
	internal static void LogVerboseDetail(string message)
	{
		if (Verbose)
			WriteLine("gray", message);
		Log.Verbose(message);
	}

	internal static void LogWarning(string message)
	{
		WriteLine("yellow", message);
		Log.Warning(message);
	}

	internal static void LogError(string message)
	{
		WriteLine("red", message);
		Log.Error(message);
	}

	internal static void LogCode(string message)
	{
		WriteLine("cyan", message);
		Log.Debug(Environment.NewLine + message);
	}

	/// <summary>
	/// Writes a labeled diagnostic block to the console when <see cref="Diagnostic"/> is enabled.
	/// Prints the label as a header line, then the content, then a closing rule line.
	/// </summary>
	internal static void LogDiagnosticBlock(string label, string content)
	{
		if (!Diagnostic)
			return;

		var rule = new string('─', 60);
		AnsiConsole.MarkupLine($"[gray]┌─ {Markup.Escape(label)} {rule}[/]");
		AnsiConsole.MarkupLine($"[gray]{Markup.Escape(content.Trim())}[/]");
		AnsiConsole.MarkupLine($"[gray]└─{rule}[/]");
	}

	/// <summary>
	/// Writes a Spectre.Console markup line to the console.
	/// </summary>
	internal static void LogMarkup(string markup)
	{
		AnsiConsole.MarkupLine(markup);
	}

	private static void WriteLine(string color, string message)
	{
		AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(message)}[/]");
	}
}
