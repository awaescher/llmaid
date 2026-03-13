using System.Text.RegularExpressions;

namespace llmaid;

/// <summary>
/// Extracts code blocks from text in various formats (XML or Markdown).
/// </summary>
public static partial class CodeBlockExtractor
{
	[GeneratedRegex(@"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`")]
	private static partial Regex MarkdownCodeBlockMatchPattern();

	/// <summary>
	/// Extracts code blocks from text, preferring XML format over Markdown.
	/// </summary>
	/// <param name="text">The source text to extract code blocks from.</param>
	/// <param name="tagNameForXmlBlock">The XML tag name to look for when extracting from XML blocks. Defaults to "file".</param>
	/// <returns>The extracted code block content, or an empty string if no valid blocks are found.</returns>
	public static string Extract(string text, string tagNameForXmlBlock = "file")
	{
		if (ExtractXml(text, tagNameForXmlBlock) is string s && s.Length > 0)
			return s;

		return ExtractMarkdown(text);
	}

	/// <summary>
	/// Extracts code blocks from XML-formatted text.
	/// </summary>
	/// <param name="text">The source text to extract code blocks from.</param>
	/// <param name="tagNameForXmlBlock">The XML tag name to look for. Defaults to "file".</param>
	/// <returns>The extracted code block content, or an empty string if no valid blocks are found.</returns>
	public static string ExtractXml(string text, string tagNameForXmlBlock = "file")
	{
		if (text.Contains($"<{tagNameForXmlBlock}>"))
		{
			var xmlMatch = new Regex($"<{tagNameForXmlBlock}>\\n?([\\s\\S]*?)\\n?<\\/{tagNameForXmlBlock}>").Matches(text).FirstOrDefault(m => m.Groups.Count > 1);
			if (xmlMatch is not null)
				return xmlMatch.Groups[1]?.Value.Trim() ?? string.Empty;
		}

		return string.Empty;
	}

	/// <summary>
	/// Extracts code blocks from Markdown-formatted text.
	/// </summary>
	/// <param name="text">The source text to extract code blocks from.</param>
	/// <returns>The extracted code block content, or an empty string if no valid blocks are found.</returns>
	public static string ExtractMarkdown(string text)
	{
		return MarkdownCodeBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1)?.Groups[1]?.Value.Trim() ?? string.Empty;
	}
}