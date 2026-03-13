using System.Text;
using llmaid;
using Shouldly;

namespace Tests;

public class FileHelperEncodingTests
{
	public class ReadFileWithEncodingAsyncMethod : FileHelperEncodingTests
	{
		private string _tempFile = null!;

		[SetUp]
		public void SetUp()
		{
			_tempFile = Path.GetTempFileName();
		}

		[TearDown]
		public void TearDown()
		{
			if (File.Exists(_tempFile))
				File.Delete(_tempFile);
		}

		[Test]
		public async Task Reads_Utf8_Without_Bom()
		{
			var expected = "Hello, World! äöü";
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			await File.WriteAllTextAsync(_tempFile, expected, encoding);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			content.ShouldBe(expected);
			detectedEncoding.GetPreamble().Length.ShouldBe(0, "UTF-8 without BOM should not have a preamble");
		}

		[Test]
		public async Task Reads_Utf8_With_Bom()
		{
			var expected = "Hello, World! äöü";
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
			await File.WriteAllTextAsync(_tempFile, expected, encoding);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			content.ShouldBe(expected);
			detectedEncoding.GetPreamble().Length.ShouldBeGreaterThan(0, "UTF-8 with BOM should have a preamble");
		}

		[Test]
		public async Task Preserves_Utf8_Bom_On_Roundtrip()
		{
			var text = "Roundtrip test with BOM äöü";
			var bomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
			await File.WriteAllTextAsync(_tempFile, text, bomEncoding);

			var originalBytes = await File.ReadAllBytesAsync(_tempFile);
			originalBytes[0].ShouldBe((byte)0xEF, "File should start with UTF-8 BOM");
			originalBytes[1].ShouldBe((byte)0xBB);
			originalBytes[2].ShouldBe((byte)0xBF);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);
			await File.WriteAllTextAsync(_tempFile, content, detectedEncoding);

			var roundtripBytes = await File.ReadAllBytesAsync(_tempFile);
			roundtripBytes.ShouldBe(originalBytes, "File bytes should be identical after roundtrip");
		}

		[Test]
		public async Task Preserves_Utf8_Without_Bom_On_Roundtrip()
		{
			var text = "Roundtrip test without BOM äöü";
			var noBomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			await File.WriteAllTextAsync(_tempFile, text, noBomEncoding);

			var originalBytes = await File.ReadAllBytesAsync(_tempFile);
			// Should NOT start with BOM
			(originalBytes[0] == 0xEF && originalBytes[1] == 0xBB && originalBytes[2] == 0xBF)
				.ShouldBeFalse("File should not start with UTF-8 BOM");

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);
			await File.WriteAllTextAsync(_tempFile, content, detectedEncoding);

			var roundtripBytes = await File.ReadAllBytesAsync(_tempFile);
			roundtripBytes.ShouldBe(originalBytes, "File bytes should be identical after roundtrip");
		}

		[Test]
		public async Task Reads_Utf16_Le_With_Bom()
		{
			var expected = "Hello UTF-16 LE äöü";
			var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
			await File.WriteAllTextAsync(_tempFile, expected, encoding);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			content.ShouldBe(expected);
			detectedEncoding.CodePage.ShouldBe(1200, "Should detect UTF-16 LE");
			detectedEncoding.GetPreamble().Length.ShouldBeGreaterThan(0, "UTF-16 LE with BOM should have a preamble");
		}

		[Test]
		public async Task Preserves_Utf16_Le_Bom_On_Roundtrip()
		{
			var text = "Roundtrip UTF-16 LE äöü";
			var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
			await File.WriteAllTextAsync(_tempFile, text, encoding);

			var originalBytes = await File.ReadAllBytesAsync(_tempFile);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);
			await File.WriteAllTextAsync(_tempFile, content, detectedEncoding);

			var roundtripBytes = await File.ReadAllBytesAsync(_tempFile);
			roundtripBytes.ShouldBe(originalBytes, "UTF-16 LE file bytes should be identical after roundtrip");
		}

		[Test]
		public async Task Reads_Ascii_Content_As_Utf8()
		{
			var expected = "Hello pure ASCII";
			await File.WriteAllBytesAsync(_tempFile, Encoding.ASCII.GetBytes(expected));

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			content.ShouldBe(expected);
			// ASCII content should be treated as UTF-8 (ASCII is a subset)
			detectedEncoding.WebName.ShouldBe("utf-8");
		}

		[Test]
		public async Task Reads_Empty_File()
		{
			await File.WriteAllBytesAsync(_tempFile, []);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			content.ShouldBe(string.Empty);
			detectedEncoding.WebName.ShouldBe("utf-8");
			detectedEncoding.GetPreamble().Length.ShouldBe(0, "Empty file should default to UTF-8 without BOM");
		}

		[Test]
		public async Task Reads_Latin1_Content()
		{
			// Write raw Latin-1 bytes (Windows-1252 / ISO-8859-1)
			// 0xE4 = ä, 0xF6 = ö, 0xFC = ü in Latin-1
			var latin1Bytes = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0xE4, 0xF6, 0xFC };
			await File.WriteAllBytesAsync(_tempFile, latin1Bytes);

			var (content, _) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			// The content should contain the decoded characters
			content.ShouldNotBeNullOrEmpty();
			content.ShouldStartWith("Hello");
		}

		[Test]
		public async Task Encoding_Used_For_Write_Matches_Encoding_Used_For_Read()
		{
			// This test verifies the core fix: the encoding returned should be
			// the one actually used to decode the content, not just what CharsetDetector reported.
			var text = "Test with special chars: äöü ñ é";
			var bomEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
			await File.WriteAllTextAsync(_tempFile, text, bomEncoding);

			var (content, detectedEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			// Write back and re-read to verify consistency
			await File.WriteAllTextAsync(_tempFile, content, detectedEncoding);
			var (rereadContent, rereadEncoding) = await FileHelper.ReadFileWithEncodingAsync(_tempFile, CancellationToken.None);

			rereadContent.ShouldBe(content, "Content should be identical after write-read cycle");
			rereadEncoding.WebName.ShouldBe(detectedEncoding.WebName, "Encoding should be consistent across read-write-read cycles");
			rereadEncoding.GetPreamble().Length.ShouldBe(detectedEncoding.GetPreamble().Length, "BOM behavior should be consistent");
		}
	}

	public class DetectLineEndingMethod : FileHelperEncodingTests
	{
		[Test]
		public void Detects_Crlf()
		{
			FileHelper.DetectLineEnding("line1\r\nline2\r\nline3").ShouldBe("\r\n");
		}

		[Test]
		public void Detects_Lf()
		{
			FileHelper.DetectLineEnding("line1\nline2\nline3").ShouldBe("\n");
		}

		[Test]
		public void Detects_Cr()
		{
			FileHelper.DetectLineEnding("line1\rline2\rline3").ShouldBe("\r");
		}

		[Test]
		public void Returns_Lf_For_No_Line_Endings()
		{
			FileHelper.DetectLineEnding("single line").ShouldBe("\n");
		}

		[Test]
		public void Returns_Lf_For_Empty_String()
		{
			FileHelper.DetectLineEnding("").ShouldBe("\n");
		}

		[Test]
		public void Returns_Lf_For_Only_Whitespace()
		{
			FileHelper.DetectLineEnding("   \t  ").ShouldBe("\n");
		}

		[Test]
		public void Detects_Dominant_Style_Crlf_Over_Lf()
		{
			// 2x CRLF, 1x LF → CRLF wins
			FileHelper.DetectLineEnding("line1\r\nline2\r\nline3\nline4").ShouldBe("\r\n");
		}

		[Test]
		public void Detects_Dominant_Style_Lf_Over_Crlf()
		{
			// 1x CRLF, 2x LF → LF wins
			FileHelper.DetectLineEnding("line1\r\nline2\nline3\nline4").ShouldBe("\n");
		}

		[Test]
		public void Crlf_Cr_In_Single_Sequence_Counts_As_Crlf_Not_Cr()
		{
			// \r\n should be counted as one CRLF, the \r alone should NOT also be counted as CR
			FileHelper.DetectLineEnding("line1\r\nline2").ShouldBe("\r\n");
		}

		[Test]
		public void Detects_Single_Crlf()
		{
			FileHelper.DetectLineEnding("line1\r\nline2").ShouldBe("\r\n");
		}

		[Test]
		public void Detects_Single_Lf()
		{
			FileHelper.DetectLineEnding("line1\nline2").ShouldBe("\n");
		}

		[Test]
		public void Detects_Single_Cr()
		{
			FileHelper.DetectLineEnding("line1\rline2").ShouldBe("\r");
		}

		[Test]
		public void Preserves_Non_Newline_Special_Characters()
		{
			// Tabs, form feeds, and other special chars must not interfere with detection
			FileHelper.DetectLineEnding("col1\tcol2\tcol3\r\ncol4\tcol5\tcol6").ShouldBe("\r\n");
		}
	}

	public class NormalizeLineEndingsMethod : FileHelperEncodingTests
	{
		[Test]
		public void Converts_Lf_To_Crlf()
		{
			FileHelper.NormalizeLineEndings("line1\nline2\nline3", "\r\n").ShouldBe("line1\r\nline2\r\nline3");
		}

		[Test]
		public void Converts_Crlf_To_Lf()
		{
			FileHelper.NormalizeLineEndings("line1\r\nline2\r\nline3", "\n").ShouldBe("line1\nline2\nline3");
		}

		[Test]
		public void Converts_Cr_To_Lf()
		{
			FileHelper.NormalizeLineEndings("line1\rline2\rline3", "\n").ShouldBe("line1\nline2\nline3");
		}

		[Test]
		public void Converts_Cr_To_Crlf()
		{
			FileHelper.NormalizeLineEndings("line1\rline2\rline3", "\r\n").ShouldBe("line1\r\nline2\r\nline3");
		}

		[Test]
		public void Converts_Lf_To_Cr()
		{
			FileHelper.NormalizeLineEndings("line1\nline2\nline3", "\r").ShouldBe("line1\rline2\rline3");
		}

		[Test]
		public void Converts_Crlf_To_Cr()
		{
			FileHelper.NormalizeLineEndings("line1\r\nline2\r\nline3", "\r").ShouldBe("line1\rline2\rline3");
		}

		[Test]
		public void Preserves_Lf_When_Target_Is_Lf()
		{
			FileHelper.NormalizeLineEndings("line1\nline2\n", "\n").ShouldBe("line1\nline2\n");
		}

		[Test]
		public void Preserves_Crlf_When_Target_Is_Crlf()
		{
			FileHelper.NormalizeLineEndings("line1\r\nline2\r\n", "\r\n").ShouldBe("line1\r\nline2\r\n");
		}

		[Test]
		public void Handles_Mixed_Endings_To_Crlf()
		{
			FileHelper.NormalizeLineEndings("line1\r\nline2\nline3\rline4", "\r\n").ShouldBe("line1\r\nline2\r\nline3\r\nline4");
		}

		[Test]
		public void Handles_Mixed_Endings_To_Lf()
		{
			FileHelper.NormalizeLineEndings("line1\r\nline2\nline3\rline4", "\n").ShouldBe("line1\nline2\nline3\nline4");
		}

		[Test]
		public void Does_Not_Double_Crlf_On_Mixed_Input()
		{
			// A CRLF input normalized to CRLF must not produce \r\r\n
			FileHelper.NormalizeLineEndings("line1\r\nline2", "\r\n").ShouldBe("line1\r\nline2");
		}

		[Test]
		public void Preserves_Non_Newline_Special_Characters()
		{
			// Tabs, null bytes and other chars must pass through unchanged
			FileHelper.NormalizeLineEndings("col1\tcol2\r\ncol3\tcol4", "\n").ShouldBe("col1\tcol2\ncol3\tcol4");
		}

		[Test]
		public void Handles_Empty_String()
		{
			FileHelper.NormalizeLineEndings("", "\r\n").ShouldBe("");
		}

		[Test]
		public void Handles_String_Without_Line_Endings()
		{
			FileHelper.NormalizeLineEndings("no newlines here", "\r\n").ShouldBe("no newlines here");
		}

		[Test]
		public void Handles_Only_Newlines()
		{
			FileHelper.NormalizeLineEndings("\n\n\n", "\r\n").ShouldBe("\r\n\r\n\r\n");
		}

		[Test]
		public void Trailing_Newline_Is_Preserved()
		{
			FileHelper.NormalizeLineEndings("line1\nline2\n", "\r\n").ShouldBe("line1\r\nline2\r\n");
		}
	}
}
