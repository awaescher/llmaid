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
}
