using System.Text;
using Microsoft.Extensions.AI;

namespace llmaid.Streaming;

/// <summary>
/// A builder that can append streamed completion updates to one single completion update
/// </summary>
public class StreamingChatCompletionUpdateBuilder
{
	private readonly StringBuilder _contentBuilder = new();
	private StreamingChatCompletionUpdate? _first;

	/// <summary>
	/// Appends a completion update to build one single completion update item
	/// </summary>
	/// <param name="update">The completion update to append to the final completion update</param>
	public void Append(StreamingChatCompletionUpdate? update)
	{
		if (update is null)
			return;

		_contentBuilder.Append(update.Text ?? "");

		_first ??= update;

		_first.AdditionalProperties = update.AdditionalProperties;
		_first.AuthorName = update.AuthorName;
		_first.ChoiceIndex = update.ChoiceIndex;
		_first.CompletionId = update.CompletionId;
		_first.CreatedAt = update.CreatedAt;
		_first.FinishReason = update.FinishReason;
		_first.Role = update.Role;

		//_first.Contents and .Text will be set in Complete() with values collected from each update
		//_first.RawRepresentation makes no sense 

		if (update.Contents is not null)
			Contents.AddRange(update.Contents);
	}

	/// <summary>
	/// Builds the final completion update out of the streamed updates that were appended before
	/// </summary>
	public StreamingChatCompletionUpdate? Complete()
	{
		if (_first is null)
			return null;

		_first.Text = _contentBuilder.ToString();
		_first.Contents = Contents;

		return _first;
	}

	/// <summary>
	/// Gets or sets the list of all content elements received from completion updates
	/// </summary>
	public List<AIContent> Contents { get; set; } = [];
}