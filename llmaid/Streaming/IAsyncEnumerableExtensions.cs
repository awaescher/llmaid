using Microsoft.Extensions.AI;
using OllamaSharp;

namespace llmaid.Streaming;

/// <summary>
/// Extension methods to stream IAsyncEnumerable to its end and return one single result value
/// </summary>
public static class IAsyncEnumerableExtensions
{
	/// <summary>
	/// Streams a given IAsyncEnumerable to its end and appends its items to a single response object
	/// </summary>
	/// <param name="stream">The IAsyncEnumerable to stream</param>
	/// <param name="itemCallback">An optional callback to additionally process every single item from the IAsyncEnumerable</param>
	/// <returns>A single response stream appened from every IAsyncEnumerable item</returns>
	public static Task<StreamingChatCompletionUpdate?> StreamToEnd(this IAsyncEnumerable<StreamingChatCompletionUpdate> stream, Action<StreamingChatCompletionUpdate>? itemCallback = null)
		=> stream.StreamToEnd(new StreamingChatCompletionUpdateAppender(), itemCallback);
}
