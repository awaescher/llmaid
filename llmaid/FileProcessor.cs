using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using Spectre.Console;

namespace llmaid;

/// <summary>
/// Holds the result of a single file-processing cycle (streaming phase only).
/// The caller decides whether and when to write the response to disk.
/// </summary>
internal record ProcessResult(
	bool Success,
	string ResponseText,
	string? OriginalCode,
	Encoding? OriginalEncoding,
	bool IsImage);

/// <summary>
/// Processes individual files by sending them to the LLM and handling the response.
/// Manages streaming, progress display, token tracking, and file writing operations.
/// </summary>
internal class FileProcessor
{
	// Display symbols for token usage output
	private const string SYM_UP = "\u2191";       // ↑ input tokens (sent to model)
	private const string SYM_DOWN = "\u2193";     // ↓ output tokens (received from model)
	private const string SYM_PIPE = "\u2502";     // │ separator
	private const string SYM_TOTAL = "\u03A3";    // Σ total tokens

	private readonly IChatClient _chatClient;
	private readonly Settings _settings;
	private readonly Stopwatch _totalStopwatch;

	// Cumulative token tracking across all files
	private long _cumulativeInputTokens;
	private long _cumulativeOutputTokens;
	private long _cumulativeReasoningTokens;
	private long _cumulativeTotalTokens;

	internal FileProcessor(IChatClient chatClient, Settings settings, Stopwatch totalStopwatch)
	{
		_chatClient = chatClient;
		_settings = settings;
		_totalStopwatch = totalStopwatch;
	}

	/// <summary>
	/// Processes a single file: reads it, sends it to the LLM, and returns a
	/// <see cref="ProcessResult"/> with the raw LLM response and original content.
	/// The caller is responsible for writing the result to disk via
	/// <see cref="WriteResponseAsync"/>.
	/// Returns a failed <see cref="ProcessResult"/> when streaming could not complete.
	/// </summary>
	internal async Task<ProcessResult> ProcessAsync(string file, string retryMessage, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		var isImage = ImageHelper.IsImageFile(file);

		// --- Read file content ---
		var (originalCode, originalEncoding) = await ReadFileContentAsync(file, isImage, cancellationToken);
		if (originalCode == null && !isImage)
			return new ProcessResult(Success: false, ResponseText: string.Empty, OriginalCode: null, OriginalEncoding: null, IsImage: isImage);

		if (!isImage && !ValidateFileTokenCount(file, originalCode!))
			return new ProcessResult(Success: false, ResponseText: string.Empty, OriginalCode: null, OriginalEncoding: null, IsImage: isImage);

		// --- Build messages ---
		var systemPrompt = BuildSystemPrompt(file, originalCode, isImage);
		var userContent = await BuildUserContentAsync(file, originalCode, cancellationToken);

		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, systemPrompt)
		};

		// --- Estimate tokens ---
		var tokenEstimates = CalculateTokenEstimates(systemPrompt, originalCode, isImage);
		LogTokenEstimates(tokenEstimates, isImage);

		var options = new ChatOptions { Temperature = _settings.Temperature }
			.AddOllamaOption(OllamaOption.NumCtx, Math.Max(tokenEstimates.ContextLength, _settings.OllamaMinNumCtx));

		// --- Stream response ---
		var streamResult = await StreamResponseAsync(messages, userContent, options, retryMessage, tokenEstimates.ResponseTokens, cancellationToken);
		if (streamResult == null)
			return new ProcessResult(Success: false, ResponseText: string.Empty, originalCode, originalEncoding, isImage);

		// --- Process token usage ---
		LogTokenUsage(streamResult, tokenEstimates.InputTokens, stopwatch);

		return new ProcessResult(true, streamResult.ResponseText, originalCode, originalEncoding, isImage);
	}

	/// <summary>
	/// Applies the LLM response from a completed <see cref="ProcessResult"/>:
	/// either writes the extracted code block back to disk (when
	/// <c>applyCodeblock</c> is true) or prints the response to the console.
	/// Returns <c>true</c> on success; <c>false</c> when the code block could
	/// not be extracted from the response.
	/// </summary>
	internal async Task<bool> WriteResponseAsync(string file, ProcessResult result, CancellationToken cancellationToken)
	{
		return await HandleResponseAsync(file, result.ResponseText, result.OriginalCode, result.OriginalEncoding, result.IsImage, cancellationToken);
	}

	// ──────────────────────────────────────────────────────────────────────
	// File reading
	// ──────────────────────────────────────────────────────────────────────

	private async Task<(string? code, Encoding? encoding)> ReadFileContentAsync(string file, bool isImage, CancellationToken cancellationToken)
	{
		if (isImage)
		{
			var fileInfo = new FileInfo(file);
			ConsoleLogger.LogVerboseDetail($"Processing image file ({fileInfo.Length / 1024.0:0.#} kB)");
			return (null, null);
		}

		try
		{
			var (code, encoding) = await FileHelper.ReadFileWithEncodingAsync(file, cancellationToken);
			ConsoleLogger.LogVerboseDetail($"Detected encoding: {encoding.EncodingName}");

			if (string.IsNullOrEmpty(code))
			{
				ConsoleLogger.LogWarning($"Skipped file {file}: No content.");
				return (null, null);
			}

			return (code, encoding);
		}
		catch (Exception ex)
		{
			ConsoleLogger.LogError($"Could not read {file}: {ex.Message}");
			return (null, null);
		}
	}

	private bool ValidateFileTokenCount(string file, string code)
	{
		var fileTokens = FileHelper.CountTokens(code);
		var maxTokens = _settings.MaxFileTokens ?? 102400;

		if (fileTokens > maxTokens)
		{
			ConsoleLogger.LogWarning($"Skipped file {file}: {fileTokens} tokens exceeds maximum of {maxTokens} tokens.");
			return false;
		}

		return true;
	}

	// ──────────────────────────────────────────────────────────────────────
	// Prompt building
	// ──────────────────────────────────────────────────────────────────────

	private string BuildSystemPrompt(string file, string? originalCode, bool isImage)
	{
		var codeLanguage = isImage ? "" : FileHelper.GetCodeLanguageByFileExtension(Path.GetExtension(file));

		return PromptPlaceholders.Replace(_settings.SystemPrompt ?? string.Empty)
			.Replace("{{CODE}}", originalCode ?? "")
			.Replace("{{CODELANGUAGE}}", codeLanguage)
			.Replace("{{FILENAME}}", Path.GetFileName(file));
	}

	private async Task<IList<AIContent>> BuildUserContentAsync(string file, string? textContent, CancellationToken cancellationToken)
	{
		var contents = new List<AIContent>();
		var fileName = Path.GetFileName(file);

		if (ImageHelper.IsImageFile(file))
		{
			contents.Add(new TextContent($"Image file: {fileName}"));

			var maxDimension = _settings.MaxImageDimension ?? 2048;
			var imageContent = await ImageHelper.LoadContentAsync(file, maxDimension, cancellationToken);
			contents.Add(imageContent);
		}
		else
		{
			var codeLanguage = FileHelper.GetCodeLanguageByFileExtension(Path.GetExtension(file));
			var userPrompt = $"""
				{fileName}
				``` {codeLanguage}
				{textContent}
				```
				""";
			contents.Add(new TextContent(userPrompt));
		}

		return contents;
	}

	// ──────────────────────────────────────────────────────────────────────
	// Token estimation
	// ──────────────────────────────────────────────────────────────────────

	private record TokenEstimates(int SystemPromptTokens, int UserPromptTokens, int InputTokens, int ResponseTokens, int ContextLength);

	private TokenEstimates CalculateTokenEstimates(string systemPrompt, string? originalCode, bool isImage)
	{
		var systemPromptTokens = FileHelper.CountTokens(systemPrompt);
		var userPromptTokens = isImage ? 500 : FileHelper.CountTokens(originalCode ?? "");
		var inputTokens = systemPromptTokens + userPromptTokens;

		var estimatedResponseTokens = isImage
			? FileHelper.CountTokens(_settings.SystemPrompt ?? "") + 200
			: FileHelper.EstimateResponseTokens(_settings, originalCode ?? "");

		var estimatedContextLength = isImage
			? Math.Max(4096, systemPromptTokens + 1000 + estimatedResponseTokens)
			: FileHelper.EstimateContextLength(originalCode ?? "", systemPrompt, estimatedResponseTokens);

		return new TokenEstimates(systemPromptTokens, userPromptTokens, inputTokens, estimatedResponseTokens, estimatedContextLength);
	}

	private static void LogTokenEstimates(TokenEstimates estimates, bool isImage)
	{
		ConsoleLogger.LogVerboseDetail($"Calculated input tokens:  {estimates.InputTokens} (prompt: {estimates.SystemPromptTokens}, file: {estimates.UserPromptTokens}{(isImage ? " estimated for image" : "")})");
		ConsoleLogger.LogVerboseDetail($"Estimated output tokens:  {estimates.ResponseTokens}");
		ConsoleLogger.LogVerboseDetail($"Estimated context length: {estimates.ContextLength} tokens");
	}

	// ──────────────────────────────────────────────────────────────────────
	// Streaming
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Thrown when the model exceeds the configured reasoning timeout.
	/// </summary>
	private sealed class ReasoningTimeoutException : Exception
	{
		public ReasoningTimeoutException(int seconds) : base($"Model reasoning exceeded the timeout of {seconds} seconds.") { }
	}

	/// <summary>
	/// Holds the result of streaming the LLM response.
	/// </summary>
	private record StreamResult(string ResponseText, ChatResponse? ChatResponse, StringBuilder ReasoningContent, int ReasoningUpdatesCount);

	private async Task<StreamResult?> StreamResponseAsync(
		List<ChatMessage> messages,
		IList<AIContent> userContent,
		ChatOptions options,
		string retryMessage,
		int estimatedResponseTokens,
		CancellationToken cancellationToken)
	{
		var generatedCodeBuilder = new StringBuilder();
		var reasoningContentBuilder = new StringBuilder();
		var reasoningUpdatesCount = 0;
		var streamingUpdates = new List<ChatResponseUpdate>();

		var showProgress = _settings.ShowProgress ?? true;
		var reasoningTimeoutSeconds = _settings.ReasoningTimeoutSeconds ?? 600;
		var reasoningTimeoutEnabled = reasoningTimeoutSeconds > 0;

		// Diagnostic: log system prompt and user content before streaming
		LogDiagnosticRequest(messages, userContent, retryMessage);

		try
		{
			if (showProgress)
				await StreamWithProgressAsync(messages, userContent, options, retryMessage, estimatedResponseTokens, reasoningTimeoutSeconds, reasoningTimeoutEnabled, generatedCodeBuilder, reasoningContentBuilder, streamingUpdates, v => reasoningUpdatesCount += v, cancellationToken);
			else
				await StreamWithoutProgressAsync(messages, userContent, options, retryMessage, reasoningTimeoutSeconds, reasoningTimeoutEnabled, generatedCodeBuilder, reasoningContentBuilder, streamingUpdates, v => reasoningUpdatesCount += v, cancellationToken);
		}
		catch (ReasoningTimeoutException ex)
		{
			ConsoleLogger.LogWarning($"Reasoning timeout after {reasoningTimeoutSeconds}s: {ex.Message}");
			return null;
		}

		var response = streamingUpdates.ToChatResponse();
		var responseText = response?.Text ?? "";

		// Diagnostic: log full response after streaming
		ConsoleLogger.LogDiagnosticBlock("Response", responseText);

		return new StreamResult(responseText, response, reasoningContentBuilder, reasoningUpdatesCount);
	}

	/// <summary>
	/// Logs the system prompt and user content to the console when diagnostic mode is enabled.
	/// Text image content is summarised as "[image omitted]" to avoid binary noise.
	/// </summary>
	private static void LogDiagnosticRequest(List<ChatMessage> messages, IList<AIContent> userContent, string retryMessage)
	{
		if (!ConsoleLogger.Diagnostic)
			return;

		var systemPrompt = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
		ConsoleLogger.LogDiagnosticBlock("System", systemPrompt);

		var userText = new StringBuilder();
		foreach (var content in userContent)
		{
			if (content is TextContent tc)
				userText.AppendLine(tc.Text);
			else
				userText.AppendLine("[image omitted]");
		}

		if (!string.IsNullOrWhiteSpace(retryMessage))
		{
			userText.AppendLine();
			userText.AppendLine(retryMessage);
		}

		ConsoleLogger.LogDiagnosticBlock("User", userText.ToString());
	}

	private async Task StreamWithProgressAsync(
		List<ChatMessage> messages,
		IList<AIContent> userContent,
		ChatOptions options,
		string retryMessage,
		int estimatedResponseTokens,
		int reasoningTimeoutSeconds,
		bool reasoningTimeoutEnabled,
		StringBuilder generatedCodeBuilder,
		StringBuilder reasoningContentBuilder,
		List<ChatResponseUpdate> streamingUpdates,
		Action<int> addReasoningUpdate,
		CancellationToken cancellationToken)
	{
		await AnsiConsole.Progress()
			.AutoClear(true)
			.Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn(), new SpinnerColumn())
			.StartAsync(async ctx =>
			{
				var sendTask = ctx.AddTask("[green]Sending[/]");
				var reasoningTask = ctx.AddTask("[green]Reasoning[/]");
				var receiveTask = ctx.AddTask("[green]Receiving[/]");

				while (!ctx.IsFinished)
				{
					AddMessagesToConversation(messages, userContent, retryMessage);

					var receivedFirstOutputToken = false;
					var reasoningStopwatch = Stopwatch.StartNew();

					await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
					{
						sendTask.Increment(100);
						streamingUpdates.Add(update);

						var hasReasoningContent = update?.Contents?.Any(c => c is TextReasoningContent) ?? false;
						var hasTextContent = !string.IsNullOrEmpty(update?.Text);

						if (hasReasoningContent)
						{
							foreach (var content in update!.Contents.OfType<TextReasoningContent>())
								reasoningContentBuilder.Append(content.Text ?? "");

							addReasoningUpdate(1);
							if (reasoningTask.Value == 0)
								reasoningTask.IsIndeterminate(true);
						}
						else if (!hasTextContent && !receivedFirstOutputToken)
						{
							addReasoningUpdate(1);
							if (reasoningTask.Value == 0)
								reasoningTask.IsIndeterminate(true);
						}

						if (hasTextContent)
						{
							if (!receivedFirstOutputToken)
							{
								receivedFirstOutputToken = true;
								reasoningStopwatch.Stop();
								reasoningTask.IsIndeterminate(false);
								reasoningTask.Value = 100;
							}

							generatedCodeBuilder.Append(update?.Text ?? "");
							receiveTask.Value = CalculateProgress(generatedCodeBuilder.Length, estimatedResponseTokens);
						}

						if (!receivedFirstOutputToken && reasoningTimeoutEnabled && reasoningStopwatch.Elapsed.TotalSeconds > reasoningTimeoutSeconds)
							throw new ReasoningTimeoutException(reasoningTimeoutSeconds);
					}

					reasoningTask.IsIndeterminate(false);
					reasoningTask.Value = 100;

					PrependAssistantStarter(generatedCodeBuilder);
					receiveTask.Value = 100;
				}
			});
	}

	private async Task StreamWithoutProgressAsync(
		List<ChatMessage> messages,
		IList<AIContent> userContent,
		ChatOptions options,
		string retryMessage,
		int reasoningTimeoutSeconds,
		bool reasoningTimeoutEnabled,
		StringBuilder generatedCodeBuilder,
		StringBuilder reasoningContentBuilder,
		List<ChatResponseUpdate> streamingUpdates,
		Action<int> addReasoningUpdate,
		CancellationToken cancellationToken)
	{
		AddMessagesToConversation(messages, userContent, retryMessage);

		var receivedFirstOutputToken = false;
		var reasoningStopwatch = Stopwatch.StartNew();

		await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
		{
			streamingUpdates.Add(update);

			var hasReasoningContent = update?.Contents?.Any(c => c is TextReasoningContent) ?? false;
			var hasTextContent = !string.IsNullOrEmpty(update?.Text);

			foreach (var content in update?.Contents?.OfType<TextReasoningContent>() ?? [])
				reasoningContentBuilder.Append(content.Text ?? "");

			if (hasReasoningContent || (!hasTextContent && !receivedFirstOutputToken))
				addReasoningUpdate(1);

			if (hasTextContent)
			{
				if (!receivedFirstOutputToken)
					reasoningStopwatch.Stop();

				receivedFirstOutputToken = true;
				generatedCodeBuilder.Append(update?.Text ?? "");
			}

			if (!receivedFirstOutputToken && reasoningTimeoutEnabled && reasoningStopwatch.Elapsed.TotalSeconds > reasoningTimeoutSeconds)
				throw new ReasoningTimeoutException(reasoningTimeoutSeconds);
		}

		PrependAssistantStarter(generatedCodeBuilder);
	}

	private void AddMessagesToConversation(List<ChatMessage> messages, IList<AIContent> userContent, string retryMessage)
	{
		messages.Add(new ChatMessage(ChatRole.User, userContent));

		if (!string.IsNullOrEmpty(retryMessage))
			messages.Add(new ChatMessage(ChatRole.User, retryMessage));

		if (!string.IsNullOrWhiteSpace(_settings.AssistantStarter))
			messages.Add(new ChatMessage(ChatRole.Assistant, _settings.AssistantStarter));
	}

	private void PrependAssistantStarter(StringBuilder generatedCodeBuilder)
	{
		if (!string.IsNullOrWhiteSpace(_settings.AssistantStarter))
			generatedCodeBuilder.Insert(0, _settings.AssistantStarter);
	}

	// ──────────────────────────────────────────────────────────────────────
	// Token usage reporting
	// ──────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Accumulates judge token usage into the cumulative totals and logs a "Judge:" token line.
	/// </summary>
	internal void AccumulateJudgeTokens(UsageDetails? usage, Stopwatch judgeStopwatch)
	{
		if (usage is null)
			return;

		var inputTokens = (int)(usage.InputTokenCount ?? 0);
		var outputTokens = (int)(usage.OutputTokenCount ?? 0);
		var totalTokens = (int)(usage.TotalTokenCount ?? (inputTokens + outputTokens));

		_cumulativeInputTokens += inputTokens;
		_cumulativeOutputTokens += outputTokens;
		_cumulativeTotalTokens += totalTokens;

		var showProgress = _settings.ShowProgress ?? true;
		if ((ConsoleLogger.Verbose || showProgress) && !_settings.DryRun)
		{
			ConsoleLogger.LogMarkup($"[gray]Judge:   {judgeStopwatch.Elapsed:hh':'mm':'ss}   [/][blue]{SYM_UP} {inputTokens,6:N0}[/]   [cyan]{SYM_DOWN} {outputTokens,6:N0}[/]   [gray]{SYM_PIPE}[/]   [yellow]{SYM_TOTAL} {totalTokens,6:N0}[/]");
			ConsoleLogger.LogMarkup($"[gray]Total:   {_totalStopwatch.Elapsed:hh':'mm':'ss}   [/][blue]{SYM_UP} {_cumulativeInputTokens,6:N0}[/]   [cyan]{SYM_DOWN} {_cumulativeOutputTokens,6:N0}[/]   [gray]{SYM_PIPE}[/]   [yellow]{SYM_TOTAL} {_cumulativeTotalTokens,6:N0}[/]");
		}
	}

	private void LogTokenUsage(StreamResult streamResult, int estimatedInputTokens, Stopwatch stopwatch)
	{
		var apiUsage = streamResult.ChatResponse?.Usage;

		var actualInputTokens = (int)(apiUsage?.InputTokenCount ?? estimatedInputTokens);
		var actualOutputTokens = (int)(apiUsage?.OutputTokenCount ?? FileHelper.CountTokens(streamResult.ResponseText));
		var actualTotalTokens = (int)(apiUsage?.TotalTokenCount ?? (actualInputTokens + actualOutputTokens));
		var reasoningTokens = ResolveReasoningTokens(apiUsage, streamResult.ReasoningContent, streamResult.ReasoningUpdatesCount);

		_cumulativeInputTokens += actualInputTokens;
		_cumulativeOutputTokens += actualOutputTokens;
		_cumulativeReasoningTokens += reasoningTokens;
		_cumulativeTotalTokens += actualTotalTokens;

		if (apiUsage is null)
		{
			ConsoleLogger.LogVerboseDetail("No API usage tokens found");
		}
		else
		{
			var reasoningPart = reasoningTokens > 0 ? $", of which reasoning: {reasoningTokens}" : "";
			ConsoleLogger.LogVerboseDetail($"Actual API usage tokens: {actualTotalTokens} (input: {actualInputTokens} + output: {actualOutputTokens}{reasoningPart})");
		}

		if (_settings.WriteResponseToConsole ?? false)
		{
			ConsoleLogger.LogVerboseInfo("");
			ConsoleLogger.LogCode(streamResult.ResponseText);
		}

		var showProgress = _settings.ShowProgress ?? true;
		if ((ConsoleLogger.Verbose || showProgress) && !_settings.DryRun)
		{
			ConsoleLogger.LogMarkup($"[gray]File:    {stopwatch.Elapsed:hh':'mm':'ss}   [/][blue]{SYM_UP} {actualInputTokens,6:N0}[/]   [cyan]{SYM_DOWN} {actualOutputTokens,6:N0}[/]   [gray]{SYM_PIPE}[/]   [yellow]{SYM_TOTAL} {actualTotalTokens,6:N0}[/]");
			ConsoleLogger.LogMarkup($"[gray]Total:   {_totalStopwatch.Elapsed:hh':'mm':'ss}   [/][blue]{SYM_UP} {_cumulativeInputTokens,6:N0}[/]   [cyan]{SYM_DOWN} {_cumulativeOutputTokens,6:N0}[/]   [gray]{SYM_PIPE}[/]   [yellow]{SYM_TOTAL} {_cumulativeTotalTokens,6:N0}[/]");
		}
	}

	/// <summary>
	/// Determines reasoning tokens using multiple fallback strategies:
	/// 1. Direct ReasoningTokenCount property (preferred)
	/// 2. AdditionalCounts dictionary entries (legacy/alternative key names)
	/// 3. Count tokens from streamed TextReasoningContent
	/// 4. Use streaming update count as estimate (each event ≈ one token)
	/// </summary>
	private static long ResolveReasoningTokens(UsageDetails? apiUsage, StringBuilder reasoningContent, int reasoningUpdatesCount)
	{
		var reasoningTokens = apiUsage?.ReasoningTokenCount ?? 0L;

		if (reasoningTokens == 0)
		{
			if (apiUsage?.AdditionalCounts?.TryGetValue("reasoning_tokens", out var tokens) == true)
				reasoningTokens = tokens;
			else if (apiUsage?.AdditionalCounts?.TryGetValue("ReasoningTokens", out tokens) == true)
				reasoningTokens = tokens;
		}

		if (reasoningTokens == 0 && reasoningContent.Length > 0)
			reasoningTokens = FileHelper.CountTokens(reasoningContent.ToString());

		if (reasoningTokens == 0 && reasoningUpdatesCount > 0)
			reasoningTokens = reasoningUpdatesCount;

		return reasoningTokens;
	}

	// ──────────────────────────────────────────────────────────────────────
	// Response handling
	// ──────────────────────────────────────────────────────────────────────

	private async Task<bool> HandleResponseAsync(string file, string responseText, string? originalCode, Encoding? originalEncoding, bool isImage, CancellationToken cancellationToken)
	{
		if ((_settings.ApplyCodeblock ?? true) && !isImage)
			return await ApplyCodeBlockAsync(file, responseText, originalCode, originalEncoding, cancellationToken);

		// Standard output for images or when ApplyCodeblock is false
		ConsoleLogger.LogResult(responseText);
		return true;
	}

	private async Task<bool> ApplyCodeBlockAsync(string file, string responseText, string? originalCode, Encoding? originalEncoding, CancellationToken cancellationToken)
	{
		var extractedCode = CodeBlockExtractor.Extract(responseText);

		if (string.IsNullOrWhiteSpace(extractedCode))
		{
			var wasOkay = responseText.Trim().EndsWith("[OK]", StringComparison.OrdinalIgnoreCase)
				|| responseText.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);

			if (wasOkay)
			{
				ConsoleLogger.LogResult("no changes");
				return true;
			}

			ConsoleLogger.LogError("Could not extract code from the model's response. It seems that there's no valid code block.");
			return false;
		}

		var finalCode = PreserveWhitespace(extractedCode, originalCode);
		var encoding = originalEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		if (originalCode != null)
		{
			var originalLineEnding = FileHelper.DetectLineEnding(originalCode);
			finalCode = FileHelper.NormalizeLineEndings(finalCode, originalLineEnding);
		}

		await File.WriteAllTextAsync(file, finalCode, encoding, cancellationToken);

		var writtenBytes = encoding.GetByteCount(finalCode) + encoding.GetPreamble().Length;
		var writtenTokens = FileHelper.CountTokens(finalCode);
		ConsoleLogger.LogResult($"{writtenBytes} bytes written ({writtenTokens} tokens)");

		return true;
	}

	private string PreserveWhitespace(string extractedCode, string? originalCode)
	{
		if ((_settings.PreserveWhitespace ?? false) && originalCode != null)
		{
			var (leading, trailing) = FileHelper.ExtractWhitespace(originalCode);
			return leading + extractedCode.Trim() + trailing;
		}

		return extractedCode;
	}

	// ──────────────────────────────────────────────────────────────────────
	// Helpers
	// ──────────────────────────────────────────────────────────────────────

	private static int CalculateProgress(double generatedChars, double estimatedTokens)
	{
		// Approximate tokens from characters (average ~4 characters per token for code)
		var estimatedGeneratedTokens = generatedChars / 4.0;
		var percentage = 0.0d;

		if (estimatedTokens > 0 && estimatedGeneratedTokens > 0)
			percentage = (estimatedGeneratedTokens / estimatedTokens) * 100;

		return Math.Min(100, Math.Max(0, (int)percentage));
	}
}
