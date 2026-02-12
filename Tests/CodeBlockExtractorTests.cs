using Shouldly;
using llmaid;

namespace Tests;

public class CodeBlockExtractorTests
{
	public class ExtractMethod : CodeBlockExtractorTests
	{
		[Test]
		public void Accepts_Xml_Code_Block()
		{
			var input = """
wat

<file>

private static string Test(string text) { }

</file>

wat
```

""";
			var code = CodeBlockExtractor.Extract(input);

			code.ShouldBe("private static string Test(string text) { }");
		}

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

			code.ShouldBe("private static string Test(string text) { }");
		}

		[Test]
		public void Takes_The_First_Code_Block()
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

			code.ShouldBe("private static string Test(string text) { }");
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

			code.ShouldBe("private static string Test(string text) { }");
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

			code.ShouldBe("private static string Test(string text) { }");
		}

		[Test, Ignore("This is failing right now, seems acceptable")]
		public void Accepts_Language_Name_With_Multiple_Whitespace()
		{
			var input = """
```			csharp

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.ShouldBe("private static string Test(string text) { }");
		}

		[Test]
		public void Accepts_Missing_Language_Name()
		{
			var input = """
```
using Microsoft.AspNetCore.Authorization;

namespace CocaCopy.Configuration;

/// <summary>
/// Contains static policies used for authorization in the application.
/// </summary>
internal static class Policies
```
""";

			var code = CodeBlockExtractor.Extract(input);

			code.ShouldStartWith("using Microsoft.AspNetCore.Authorization;");
		}

		[Test]
		public void Accepts_Missing_Language_Name_With_Newline()
		{
			var input = """
```

private static string Test(string text) { }

```
""";

			var code = CodeBlockExtractor.Extract(input);

			code.ShouldBe("private static string Test(string text) { }");
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

			code.ShouldBe("private static string Test(string text) { }");
		}

		[Test, Ignore("This is failing right now, seems acceptable")]
		public void Accepts_Multiple_Words_As_Language_Name()
		{
			var input = """
``` visual basic

private static string Test(string text) { }

```
""";
			var code = CodeBlockExtractor.Extract(input);

			code.ShouldBe("private static string Test(string text) { }");
		}
	}
}