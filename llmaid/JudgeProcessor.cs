using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace llmaid;

/// <summary>
/// Represents the outcome of a judge review cycle for a single file edit.
/// </summary>
internal record JudgeVerdict(bool Passed, string[] Violations, UsageDetails? Usage)
{
	/// <summary>Gets a verdict that indicates the edit passed all checks.</summary>
	internal static JudgeVerdict Pass(UsageDetails? usage = null) => new(true, [], usage);

	/// <summary>Gets a verdict that indicates one or more rule violations were found.</summary>
	internal static JudgeVerdict Fail(string[] violations, UsageDetails? usage = null) => new(false, violations, usage);
}

/// <summary>
/// Calls a judge LLM to verify that an LLM response or git diff complies with the
/// original task instructions. Returns a <see cref="JudgeVerdict"/> that either
/// approves the edit or lists every specific violation found.
/// </summary>
internal class JudgeProcessor
{
	/// <summary>
	/// The built-in judge system prompt used for git-diff evaluation when no custom
	/// <see cref="Settings.JudgeSystemPrompt"/> is configured.
	/// </summary>
	private const string DEFAULT_DIFF_JUDGE_SYSTEM_PROMPT = """
		You are a strict review judge. You receive:
		1. The task instructions the AI editor had to follow
		2. A git diff showing what the AI editor changed in one file

		Your job: identify any changes in the diff that violate the task instructions.

		Respond with exactly one of:

		  PASS
		    — when every change in the diff complies with the instructions.

		  FAIL
		  - <violation 1>
		  - <violation 2>
		    — when one or more changes violate the instructions.
		      List every violation on its own bullet line with a dash (-)
		      and reference the specific identifier or line that was changed incorrectly.

		Be strict and precise. Any change not explicitly permitted by the instructions is a FAIL.
		""";

	/// <summary>
	/// The built-in judge system prompt used for raw-response evaluation when no custom
	/// <see cref="Settings.JudgeSystemPrompt"/> is configured.
	/// </summary>
	private const string DEFAULT_RESPONSE_JUDGE_SYSTEM_PROMPT = """
		You are a strict review judge. You receive:
		1. The task instructions the AI editor had to follow
		2. The original file content before editing
		3. The AI editor's response (the new version of the file)

		Your job: compare the original and the new version, identify every change made,
		and check whether each change complies with the task instructions.

		Respond with exactly one of:

		  PASS
		    — when every change in the response complies with the instructions.

		  FAIL
		  - <violation 1>
		  - <violation 2>
		    — when one or more changes violate the instructions.
		      List every violation on its own bullet line with a dash (-)
		      and reference the specific identifier or line that was changed incorrectly.

		Be strict and precise. Any change not explicitly permitted by the instructions is a FAIL.
		""";

	private readonly IChatClient _chatClient;
	private readonly Settings _settings;

	internal JudgeProcessor(IChatClient chatClient, Settings settings)
	{
		_chatClient = chatClient;
		_settings = settings;
	}

	/// <summary>
	/// Evaluates the raw LLM response against the original file content and task
	/// instructions, and returns a <see cref="JudgeVerdict"/> indicating whether
	/// the response should be accepted or rejected with a list of violations.
	/// This method does not require git or <c>applyCodeblock</c> to be enabled.
	/// </summary>
	/// <param name="originalCode">The original file content before the edit.</param>
	/// <param name="llmResponse">The raw response text returned by the editing LLM.</param>
	/// <param name="taskSystemPrompt">The system prompt that was given to the editing LLM.</param>
	/// <param name="cancellationToken">The token to cancel the operation with.</param>
	internal async Task<JudgeVerdict> EvaluateResponseAsync(string originalCode, string llmResponse, string taskSystemPrompt, CancellationToken cancellationToken)
	{
		var judgeSystemPrompt = string.IsNullOrWhiteSpace(_settings.JudgeSystemPrompt)
			? DEFAULT_RESPONSE_JUDGE_SYSTEM_PROMPT
			: _settings.JudgeSystemPrompt;

		var userMessage = $"""
			# Task instructions the AI editor had to follow

			{taskSystemPrompt}

			# Original file content

			```
			{originalCode}
			```

			# AI response (new version of the file)

			```
			{llmResponse}
			```
			""";

		ConsoleLogger.LogVerboseDetail("Judge: evaluating response ...");

		return await CallJudgeAsync(judgeSystemPrompt, userMessage, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Evaluates a git diff against the original task instructions and returns a
	/// <see cref="JudgeVerdict"/> indicating whether the edit should be accepted
	/// or rejected with a list of violations.
	/// </summary>
	/// <param name="diff">The raw unified diff text produced by <c>git diff HEAD</c>.</param>
	/// <param name="taskSystemPrompt">The system prompt that was given to the editing LLM.</param>
	/// <param name="cancellationToken">The token to cancel the operation with.</param>
	internal async Task<JudgeVerdict> EvaluateAsync(string diff, string taskSystemPrompt, CancellationToken cancellationToken)
	{
		var judgeSystemPrompt = string.IsNullOrWhiteSpace(_settings.JudgeSystemPrompt)
			? DEFAULT_DIFF_JUDGE_SYSTEM_PROMPT
			: _settings.JudgeSystemPrompt;

		var userMessage = $"""
			# Task instructions the AI editor had to follow

			{taskSystemPrompt}

			# Git diff of the changes made

			```diff
			{diff}
			```
			""";

		ConsoleLogger.LogVerboseDetail("Judge: evaluating diff ...");

		return await CallJudgeAsync(judgeSystemPrompt, userMessage, cancellationToken).ConfigureAwait(false);
	}

	// ──────────────────────────────────────────────────────────────────────
	// Shared LLM invocation
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Sends the assembled system prompt and user message to the judge LLM and
	/// returns the parsed <see cref="JudgeVerdict"/>. Both
	/// <see cref="EvaluateResponseAsync"/> and <see cref="EvaluateAsync"/> delegate
	/// here so the context-sizing and API call logic is not duplicated.
	/// </summary>
	private async Task<JudgeVerdict> CallJudgeAsync(string judgeSystemPrompt, string userMessage, CancellationToken cancellationToken)
	{
		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, judgeSystemPrompt),
			new(ChatRole.User, userMessage)
		};

		// Estimate a context size for the judge call and enforce the same minimum
		// as the main provider to avoid Ollama unloading the model between calls.
		var judgeInputTokens = FileHelper.CountTokens(judgeSystemPrompt) + FileHelper.CountTokens(userMessage);
		var judgeContextLength = Math.Max(judgeInputTokens + 1024, _settings.OllamaMinNumCtx);

		var options = new ChatOptions { Temperature = _settings.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, judgeContextLength);

		ConsoleLogger.LogDiagnosticBlock("Judge · System", judgeSystemPrompt);
		ConsoleLogger.LogDiagnosticBlock("Judge · User", userMessage);

		var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken: cancellationToken).ConfigureAwait(false);
		var responseText = response?.Text ?? string.Empty;

		ConsoleLogger.LogVerboseDetail($"Judge response:{Environment.NewLine}{responseText.Trim()}");
		ConsoleLogger.LogDiagnosticBlock("Judge · Response", responseText);

		return ParseVerdict(responseText, response?.Usage);
	}

	// ──────────────────────────────────────────────────────────────────────
	// Verdict parsing
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Parses the raw judge LLM response text into a structured
	/// <see cref="JudgeVerdict"/>. The expected format is either the single
	/// word <c>PASS</c> (case-insensitive) or <c>FAIL</c> followed by one
	/// or more bullet lines starting with <c>-</c>.
	/// </summary>
	internal static JudgeVerdict ParseVerdict(string responseText, UsageDetails? usage = null)
	{
		if (string.IsNullOrWhiteSpace(responseText))
			return JudgeVerdict.Fail(["Judge returned an empty response."], usage);

		var trimmed = responseText.Trim();

		// Check for PASS — scan all lines to handle models that add a preamble or thinking blocks
		var lines = trimmed.Split('\n');
		if (lines.Any(l => l.Trim().Equals("PASS", StringComparison.OrdinalIgnoreCase)))
			return JudgeVerdict.Pass(usage);

		// Parse FAIL with bullet violations
		var violations = trimmed
			.Split('\n')
			.Select(l => l.Trim())
			.Where(l => l.StartsWith("- ", StringComparison.Ordinal))
			.Select(l => l[2..].Trim())
			.Where(l => !string.IsNullOrWhiteSpace(l))
			.ToArray();

		if (violations.Length == 0)
		{
			// The model said FAIL but gave no structured violations — use the whole response as the message
			violations = [trimmed];
		}

		return JudgeVerdict.Fail(violations, usage);
	}
}
