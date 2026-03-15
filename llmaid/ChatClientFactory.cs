using System.ClientModel;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace llmaid;

/// <summary>
/// Creates an <see cref="IChatClient"/> for the configured provider (Ollama, LM Studio, OpenAI, MiniMax).
/// </summary>
internal static class ChatClientFactory
{
	private static readonly TimeSpan _networkTimeout = TimeSpan.FromMinutes(15);

	/// <summary>
	/// Creates a chat client for the main editing LLM using the primary provider settings.
	/// </summary>
	internal static IChatClient Create(Settings settings)
	{
		return Create(
			settings.Provider ?? string.Empty,
			settings.Uri,
			settings.Model ?? string.Empty,
			settings.ApiKey);
	}

	/// <summary>
	/// Creates a chat client for the judge LLM. Each judge-specific connection
	/// setting (<see cref="Settings.JudgeProvider"/>, <see cref="Settings.JudgeUri"/>,
	/// <see cref="Settings.JudgeModel"/>, <see cref="Settings.JudgeApiKey"/>) falls
	/// back to its corresponding main-provider value when not explicitly set, so only
	/// deviations from the main provider need to be specified.
	/// </summary>
	internal static IChatClient CreateJudgeClient(Settings settings)
	{
		return Create(
			settings.JudgeProvider ?? settings.Provider ?? string.Empty,
			settings.JudgeUri ?? settings.Uri,
			settings.JudgeModel ?? settings.Model ?? string.Empty,
			settings.JudgeApiKey ?? settings.ApiKey);
	}

	private static IChatClient Create(string provider, Uri? uri, string model, string? apiKey)
	{
		if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
			return CreateOllamaClient(uri, model);

		if (provider.Equals("minimax", StringComparison.OrdinalIgnoreCase))
			return CreateMiniMaxClient(uri, model, apiKey);

		if (provider.Equals("lmstudio", StringComparison.OrdinalIgnoreCase) || provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
			return CreateOpenAICompatibleClient(uri, model, apiKey);

		return CreateOpenAIClient(uri, model, apiKey);
	}

	private static IChatClient CreateOllamaClient(Uri? uri, string model)
	{
		var httpClient = new HttpClient { BaseAddress = uri, Timeout = _networkTimeout };
		return new OllamaApiClient(httpClient, model);
	}

	/// <summary>
	/// Creates a client for LM Studio and other OpenAI-compatible servers.
	/// LM Studio default endpoint: http://localhost:1234/v1.
	/// API key can be empty or any string for local servers.
	/// </summary>
	private static IChatClient CreateOpenAICompatibleClient(Uri? uri, string model, string? apiKey)
	{
		var key = string.IsNullOrWhiteSpace(apiKey) ? "-" : apiKey;
		var options = new OpenAI.OpenAIClientOptions { Endpoint = uri, NetworkTimeout = _networkTimeout };
		return new OpenAI.OpenAIClient(new ApiKeyCredential(key), options)
			.GetChatClient(model)
			.AsIChatClient();
	}

	/// <summary>
	/// Creates a client for MiniMax using its OpenAI-compatible API.
	/// Default endpoint: https://api.minimax.io/v1.
	/// API key is required and can be set via the MINIMAX_API_KEY environment variable.
	/// </summary>
	private static IChatClient CreateMiniMaxClient(Uri? uri, string model, string? apiKey)
	{
		var endpoint = uri ?? new Uri("https://api.minimax.io/v1");
		var options = new OpenAI.OpenAIClientOptions { Endpoint = endpoint, NetworkTimeout = _networkTimeout };
		return new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey ?? string.Empty), options)
			.GetChatClient(model ?? "MiniMax-M2.5")
			.AsIChatClient();
	}

	private static IChatClient CreateOpenAIClient(Uri? uri, string model, string? apiKey)
	{
		var options = new OpenAI.OpenAIClientOptions { Endpoint = uri, NetworkTimeout = _networkTimeout };
		return new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey ?? string.Empty), options)
			.GetChatClient(model)
			.AsIChatClient();
	}
}
