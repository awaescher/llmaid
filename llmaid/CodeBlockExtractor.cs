using System.Text.RegularExpressions;

namespace llmaid;

public static partial class CodeBlockExtractor
{
	[GeneratedRegex(@"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`")]
	private static partial Regex MarkdownCodeBlockMatchPattern();

	public static string Extract(string text, string tagNameForXmlBlock = "file")
	{
		if (text.Contains($"<{tagNameForXmlBlock}>"))
		{
			var xmlMatch = new Regex($"<{tagNameForXmlBlock}>\\n?([\\s\\S]*?)\\n?<\\/{tagNameForXmlBlock}>").Matches(text).FirstOrDefault(m => m.Groups.Count > 1);
			if (xmlMatch is not null)
				return xmlMatch.Groups[1]?.Value.Trim() ?? string.Empty;
		}

		return MarkdownCodeBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1)?.Groups[1]?.Value.Trim() ?? string.Empty;
	}
}
