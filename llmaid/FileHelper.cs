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
		var detectedEncoding = ResolveEncoding(detectionResult);

		using var stream = new MemoryStream(fileBytes);
		using var reader = new StreamReader(stream, detectedEncoding, detectEncodingFromByteOrderMarks: true);
		var content = await reader.ReadToEndAsync(cancellationToken);

		// Use the encoding the StreamReader actually applied. When a BOM is present,
		// StreamReader may switch to a different encoding than what CharsetDetector
		// reported, so CurrentEncoding is the authoritative source.
		var actualEncoding = reader.CurrentEncoding;

		// Preserve BOM status: if the file had a BOM (preamble bytes at the start),
		// make sure the returned encoding will emit it again on write.
		var hasBom = HasByteOrderMark(fileBytes, actualEncoding);
		var encodingForWrite = EnsureBomBehavior(actualEncoding, hasBom);

		return (content, encodingForWrite);
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

		// ASCII is a subset of UTF-8, treat as UTF-8 without BOM
		if (detectedEncoding.WebName.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		return detectedEncoding;
	}

	/// <summary>
	/// Checks whether the raw file bytes start with the BOM (preamble) of the given encoding.
	/// </summary>
	private static bool HasByteOrderMark(byte[] fileBytes, Encoding encoding)
	{
		var preamble = encoding.GetPreamble();

		if (preamble.Length == 0 || fileBytes.Length < preamble.Length)
			return false;

		return fileBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);
	}

	/// <summary>
	/// Returns an encoding instance whose BOM emission behavior matches the original file.
	/// For UTF-8 and Unicode encodings, this ensures the BOM is preserved (or omitted) on write.
	/// </summary>
	private static Encoding EnsureBomBehavior(Encoding encoding, bool hasBom)
	{
		// UTF-8: control BOM emission explicitly
		if (encoding is UTF8Encoding || encoding.WebName.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);

		// UTF-16 LE
		if (encoding.CodePage == 1200)
			return new UnicodeEncoding(bigEndian: false, byteOrderMark: hasBom);

		// UTF-16 BE
		if (encoding.CodePage == 1201)
			return new UnicodeEncoding(bigEndian: true, byteOrderMark: hasBom);

		// UTF-32 LE
		if (encoding.CodePage == 12000)
			return new UTF32Encoding(bigEndian: false, byteOrderMark: hasBom);

		// UTF-32 BE
		if (encoding.CodePage == 12001)
			return new UTF32Encoding(bigEndian: true, byteOrderMark: hasBom);

		// For single-byte encodings (Windows-1252, ISO-8859-1, etc.) BOM is not applicable
		return encoding;
	}

	/// <summary>
	/// Detects the dominant line ending style in a text string.
	/// Returns "\r\n" for Windows, "\r" for old Mac, or "\n" for Unix (default).
	/// </summary>
	internal static string DetectLineEnding(string text)
	{
		var crlf = 0;
		var lf = 0;
		var cr = 0;

		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] == '\r')
			{
				if (i + 1 < text.Length && text[i + 1] == '\n')
				{
					crlf++;
					i++; // skip the \n
				}
				else
				{
					cr++;
				}
			}
			else if (text[i] == '\n')
			{
				lf++;
			}
		}

		if (crlf == 0 && lf == 0 && cr == 0)
			return "\n";

		if (crlf >= lf && crlf >= cr)
			return "\r\n";

		if (cr > lf)
			return "\r";

		return "\n";
	}

	/// <summary>
	/// Normalizes all line endings in a text string to the specified line ending.
	/// </summary>
	internal static string NormalizeLineEndings(string text, string lineEnding)
	{
		// First normalize everything to \n, then replace with the target
		var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");

		if (lineEnding == "\n")
			return normalized;

		return normalized.Replace("\n", lineEnding);
	}
}
