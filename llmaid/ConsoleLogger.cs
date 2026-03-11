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
	/// Writes the file header (always visible, in white).
	/// </summary>
	internal static void LogFileHeader(string message)
	{
		WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes the result output (always visible, in cyan).
	/// </summary>
	internal static void LogResult(string message)
	{
		WriteLine("cyan", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes informational output only in verbose mode.
	/// </summary>
	internal static void LogVerboseInfo(string message)
	{
		if (Verbose)
			WriteLine("white", message);
		Log.Information(message);
	}

	/// <summary>
	/// Writes detailed output only in verbose mode.
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
	/// Writes a Spectre.Console markup line (raw markup, not escaped).
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
