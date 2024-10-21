using FluentAssertions;
using llmaid;

namespace Tests;

public class CodeBlockExtractorTests
{
	public class ExtractMethod : CodeBlockExtractorTests
	{
		[Test]
		public void Ignores_Text_Outside_Of_The_Code_Block()
		{
			var input = """
I am just a teenage dirtbag, baby.
```csharp

private static string Test(string text) { }

```
Listen to Iron Maiden, baby, with me.
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Code_Block_Without_Language_Name()
		{
			var input = """
```
private static string Test(string text) { }
```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}
		[Test]
		public void Accepts_Language_Name_Without_Whitespace()
		{
			var input = """
```csharp

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Language_Name_With_Single_Whitespace()
		{
			var input = """
``` csharp

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Language_Name_With_Multiple_Whitespace()
		{
			var input = """
```			csharp

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Missing_Language_Name()
		{
			var input = """
```

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Indentation()
		{
			var input = """
		```

		private static string Test(string text) { }

		```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Multiple_Words_As_Language_Name()
		{
			var input = """
``` visual basic

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.Should().Be("private static string Test(string text) { }");
		}

		[Test]
		public void Extracts_Contents_With_Code_Blocks()
		{
			var input = """
Ignore this

``` markdown
This is the code in C#:

``` csharp

private static string Test(string text) { }

```

And this in Visual Basic:

``` basic

Public Shared Function Test(text As String) As String
End Function

```

Easy, right?

```

Ignore this, too.
""";
			var code = CodeBlockExtractor.Extract(input);

			var cleaned = """
This is the code in C#:

``` csharp

private static string Test(string text) { }

```

And this in Visual Basic:

``` basic

Public Shared Function Test(text As String) As String
End Function

```

Easy, right?
""";

			code.Should().Be(cleaned);
		}
	}

	/// <summary>
	/// This is not a feature test but to document the expected behavior.
	/// In case of an invalid format, the CodeBlockExtractor may simply
	/// return whats between the first and the last three backticks.
	/// </summary>
	[Test]
	public void Wreckes_Multiple_Code_Blocks_If_There_Is_No_Surrounding_Code_Block()
	{
		var input = """
```csharp

private static string Test(string text) { }

```

```csharp

private static string AnotherTest() { }

```

""";
		var code = CodeBlockExtractor.Extract(input);

		var expected = """
private static string Test(string text) { }

```

```csharp

private static string AnotherTest() { }
""";

		code.Should().Be(expected);
	}

}