using System.Text.RegularExpressions;

namespace llmaid;

public static partial class CodeBlockExtractor
{
	[GeneratedRegex(@"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`")]
	private static partial Regex CodeBlockMatchPattern();

	public static string Extract(string text)
	{
		return CodeBlockMatchPattern().Matches(text).FirstOrDefault(m => m.Groups.Count > 1)?.Groups[1]?.Value.Trim() ?? string.Empty;
	}
}
