using System.Diagnostics;

namespace llmaid;

/// <summary>
/// Provides helpers for interacting with a local git repository.
/// All methods are thin wrappers around the <c>git</c> CLI and require
/// git to be available on the system PATH.
/// </summary>
internal static class GitHelper
{
	/// <summary>
	/// Determines whether the specified path resides inside a git repository
	/// by running <c>git rev-parse --is-inside-work-tree</c>.
	/// </summary>
	/// <param name="path">
	/// A file or directory path to check. When a file path is given its
	/// containing directory is used as the working directory for git.
	/// </param>
	/// <returns>
	/// <c>true</c> if the path is inside a git work-tree; <c>false</c> otherwise
	/// or when git is not available.
	/// </returns>
	internal static async Task<bool> IsInsideGitRepoAsync(string path)
	{
		var workingDir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;

		try
		{
			var result = await RunGitAsync(workingDir, "rev-parse --is-inside-work-tree").ConfigureAwait(false);
			return result.ExitCode == 0 && result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Returns the <c>git diff HEAD</c> output for a single file, showing all
	/// uncommitted changes relative to the last commit.
	/// </summary>
	/// <param name="file">The absolute or relative path of the file to diff.</param>
	/// <returns>
	/// The raw unified diff text, or an empty string when there are no changes
	/// or the file is not tracked by git.
	/// </returns>
	internal static async Task<string> GetDiffAsync(string file)
	{
		var workingDir = Path.GetDirectoryName(Path.GetFullPath(file))!;
		var fileName = Path.GetFileName(file);

		var result = await RunGitAsync(workingDir, $"diff HEAD -- \"{fileName}\"").ConfigureAwait(false);
		return result.ExitCode == 0 ? result.StdOut : string.Empty;
	}

	// ──────────────────────────────────────────────────────────────────────
	// Internals
	// ──────────────────────────────────────────────────────────────────────

	private record GitResult(int ExitCode, string StdOut, string StdErr);

	private static async Task<GitResult> RunGitAsync(string workingDirectory, string arguments)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "git",
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		process.Start();

		var stdOut = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
		var stdErr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

		await process.WaitForExitAsync().ConfigureAwait(false);

		return new GitResult(process.ExitCode, stdOut, stdErr);
	}
}
