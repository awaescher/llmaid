using System.ClientModel;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace llmaid;

/// <summary>
/// Creates an <see cref="IChatClient"/> for the configured provider (Ollama, LM Studio, OpenAI).
/// </summary>
internal static class ChatClientFactory
{
	private static readonly TimeSpan _networkTimeout = TimeSpan.FromMinutes(15);

	internal static IChatClient Create(Settings settings)
	{
		var provider = settings.Provider ?? string.Empty;

		if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
			return CreateOllamaClient(settings);

		if (provider.Equals("lmstudio", StringComparison.OrdinalIgnoreCase) || provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
			return CreateOpenAICompatibleClient(settings);

		return CreateOpenAIClient(settings);
	}

	private static IChatClient CreateOllamaClient(Settings settings)
	{
		var httpClient = new HttpClient { BaseAddress = settings.Uri, Timeout = _networkTimeout };
		return new OllamaApiClient(httpClient, settings.Model ?? string.Empty);
	}

	/// <summary>
	/// Creates a client for LM Studio and other OpenAI-compatible servers.
	/// LM Studio default endpoint: http://localhost:1234/v1.
	/// API key can be empty or any string for local servers.
	/// </summary>
	private static IChatClient CreateOpenAICompatibleClient(Settings settings)
	{
		var apiKey = string.IsNullOrWhiteSpace(settings.ApiKey) ? "lm-studio" : settings.ApiKey;
		var options = new OpenAI.OpenAIClientOptions { Endpoint = settings.Uri, NetworkTimeout = _networkTimeout };
		return new OpenAI.OpenAIClient(new ApiKeyCredential(apiKey), options)
			.GetChatClient(settings.Model ?? string.Empty)
			.AsIChatClient();
	}

	private static IChatClient CreateOpenAIClient(Settings settings)
	{
		var options = new OpenAI.OpenAIClientOptions { Endpoint = settings.Uri, NetworkTimeout = _networkTimeout };
		return new OpenAI.OpenAIClient(new ApiKeyCredential(settings.ApiKey ?? string.Empty), options)
			.GetChatClient(settings.Model ?? string.Empty)
			.AsIChatClient();
	}
}
