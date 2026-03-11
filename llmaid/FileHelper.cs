using System.Text;
using Microsoft.ML.Tokenizers;
using UtfUnknown;

namespace llmaid;

/// <summary>
/// Utility methods for file I/O, encoding detection, token counting, and code language mapping.
/// </summary>
internal static class FileHelper
{
	// Tokenizer for accurate token estimation (o200k_base is the standard for GPT-4o and similar models)
	private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

	/// <summary>
	/// Counts the actual number of tokens in a text using a real tokenizer.
	/// </summary>
	internal static int CountTokens(string text)
	{
		if (string.IsNullOrEmpty(text))
			return 0;

		return _tokenizer.CountTokens(text);
	}

	/// <summary>
	/// Returns a human-readable file size string (e.g. "12.3 kB").
	/// </summary>
	internal static string GetFileSizeString(string file)
	{
		try
		{
			return $"{new FileInfo(file).Length / 1024.0:0.#} kB";
		}
		catch
		{
			return "??? kB";
		}
	}

	/// <summary>
	/// Reads a file and detects its encoding. Uses UTF-Unknown for BOM and content-based detection.
	/// </summary>
	internal static async Task<(string content, Encoding encoding)> ReadFileWithEncodingAsync(string file, CancellationToken cancellationToken)
	{
		var fileBytes = await File.ReadAllBytesAsync(file, cancellationToken);

		if (fileBytes.Length == 0)
			return (string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

		var detectionResult = CharsetDetector.DetectFromBytes(fileBytes);
		var encoding = ResolveEncoding(detectionResult);

		using var stream = new MemoryStream(fileBytes);
		using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
		var content = await reader.ReadToEndAsync(cancellationToken);

		return (content, encoding);
	}

	/// <summary>
	/// Extracts the leading and trailing whitespace from a string.
	/// Used to preserve original file whitespace when replacing content.
	/// </summary>
	internal static (string leading, string trailing) ExtractWhitespace(string text)
	{
		if (string.IsNullOrEmpty(text))
			return (string.Empty, string.Empty);

		var trimmed = text.Trim();
		if (trimmed.Length == 0)
			return (text, string.Empty);

		var leadingLength = text.IndexOf(trimmed[0]);
		var trailingStart = text.LastIndexOf(trimmed[^1]) + 1;

		return (text[..leadingLength], text[trailingStart..]);
	}

	/// <summary>
	/// Estimates the number of response tokens the model will generate.
	/// </summary>
	internal static int EstimateResponseTokens(Settings settings, string code)
	{
		var codeTokens = CountTokens(code);

		if (settings.ApplyCodeblock ?? true)
			return (int)(codeTokens * 1.25f);  // The LLM will extend/modify the code
		else
			return (int)(codeTokens * 0.25f);  // Only short responses expected
	}

	/// <summary>
	/// Estimates the total context length needed for a request.
	/// </summary>
	internal static int EstimateContextLength(string originalCode, string systemPrompt, int estimatedResponseTokens)
	{
		var contextLength = CountTokens(systemPrompt) + CountTokens(originalCode) + estimatedResponseTokens;
		return Math.Max(2048, contextLength);
	}

	/// <summary>
	/// Maps a file extension to a code language identifier for markdown code blocks.
	/// </summary>
	internal static string GetCodeLanguageByFileExtension(string fileExtension)
	{
		return fileExtension.ToLower() switch
		{
			".cs" => "csharp",
			".js" => "javascript",
			".ts" => "typescript",
			".java" => "java",
			".py" => "python",
			".cpp" => "cpp",
			".c" => "c",
			".rb" => "ruby",
			".php" => "php",
			".html" => "html",
			".css" => "css",
			".xml" => "xml",
			".json" => "json",
			".sh" => "bash",
			".vb" => "vbnet",
			".md" => "markdown",
			".rs" => "rust",
			".go" => "go",
			".swift" => "swift",
			".kt" => "kotlin",
			".m" => "objectivec",
			".pl" => "perl",
			".r" => "r",
			".sql" => "sql",
			".scss" => "scss",
			".less" => "less",
			".scala" => "scala",
			".lua" => "lua",
			".dart" => "dart",
			".jsx" => "javascript",
			".tsx" => "typescript",
			".bat" => "bat",
			".cmd" => "bat",
			".ini" => "ini",
			".cfg" => "ini",
			".yaml" => "yaml",
			".yml" => "yaml",
			".h" => "c",
			".hpp" => "cpp",
			".coffee" => "coffeescript",
			".erl" => "erlang",
			".ex" => "elixir",
			".exs" => "elixir",
			".hs" => "haskell",
			".jl" => "julia",
			".ps1" => "powershell",
			".f90" => "fortran",
			".asm" => "nasm",
			".v" => "verilog",
			_ => ""
		};
	}

	private static Encoding ResolveEncoding(DetectionResult detectionResult)
	{
		if (detectionResult.Detected == null)
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		var detectedEncoding = detectionResult.Detected.Encoding;
		var hasBom = detectionResult.Detected.HasBOM;

		// ASCII is a subset of UTF-8, treat as UTF-8 without BOM
		if (detectedEncoding.WebName.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		// For UTF-8, preserve BOM status
		if (detectedEncoding is UTF8Encoding || detectedEncoding.WebName.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);

		return detectedEncoding;
	}
}
