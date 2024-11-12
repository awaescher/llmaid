using System.Text.RegularExpressions;

namespace llmaid;

public static partial class CodeBlockExtractor
{
	[GeneratedRegex(@"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`")]
	private static partial Regex CodeBlockMatchPattern();

	[GeneratedRegex(@"<file>\n?([\s\S]*?)\n?<\/file>")]
	private static partial Regex CodeXmlBlockMatchPattern();

	public static string Extract(string text)
	{
		var xmlMatch = CodeXmlBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1);
		if (xmlMatch is not null)
			return xmlMatch.Groups[1]?.Value.Trim() ?? string.Empty;

		return CodeBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1)?.Groups[1]?.Value.Trim() ?? string.Empty;
	}
}
