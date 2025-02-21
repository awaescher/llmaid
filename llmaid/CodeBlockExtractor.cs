using System.Text.RegularExpressions;

namespace llmaid;

public static partial class CodeBlockExtractor
{
	[GeneratedRegex(@"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`")]
	private static partial Regex MarkdownCodeBlockMatchPattern();

	public static string Extract(string text, string tagNameForXmlBlock = "file")
	{
		if (ExtractXml(text, tagNameForXmlBlock) is string s && s.Length > 0)
			return s;

		return ExtractMarkdown(text);
	}

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

	public static string ExtractMarkdown(string text)
	{
		return MarkdownCodeBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1)?.Groups[1]?.Value.Trim() ?? string.Empty;
	}


}
