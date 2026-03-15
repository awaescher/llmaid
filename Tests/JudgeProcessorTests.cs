using llmaid;
using Shouldly;

namespace Tests;

public class JudgeProcessorTests
{
	// ParseVerdict is shared by both EvaluateAsync (git-diff mode) and
	// EvaluateResponseAsync (response mode). All verdict-parsing tests live here
	// and apply equally to both judge modes.
	public class ParseVerdictMethod : JudgeProcessorTests
	{
		[Test]
		public void Returns_Pass_For_Plain_PASS_Response()
		{
			var verdict = llmaid.JudgeProcessor.ParseVerdict("PASS");

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
		}

		[Test]
		public void Returns_Pass_For_PASS_With_Trailing_Whitespace()
		{
			var verdict = llmaid.JudgeProcessor.ParseVerdict("  PASS  \n");

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
		}

		[Test]
		public void Returns_Pass_For_Lowercase_pass()
		{
			var verdict = llmaid.JudgeProcessor.ParseVerdict("pass");

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
		}

		[Test]
		public void Returns_Fail_With_Single_Violation()
		{
			var response = """
				FAIL
				- Variable `x` was renamed from `myVar` to `myVariable`
				""";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(1);
			verdict.Violations[0].ShouldBe("Variable `x` was renamed from `myVar` to `myVariable`");
		}

		[Test]
		public void Returns_Fail_With_Multiple_Violations()
		{
			var response = """
				FAIL
				- Import statement was removed on line 3
				- Method body was reformatted changing indentation
				- Private field `_cache` was given a new XML doc comment
				""";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(3);
			verdict.Violations[0].ShouldBe("Import statement was removed on line 3");
			verdict.Violations[1].ShouldBe("Method body was reformatted changing indentation");
			verdict.Violations[2].ShouldBe("Private field `_cache` was given a new XML doc comment");
		}

		[Test]
		public void Returns_Fail_With_Raw_Text_When_No_Bullet_Lines_Present()
		{
			var response = "FAIL because the model changed executable code on line 12.";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(1);
			verdict.Violations[0].ShouldBe(response);
		}

		[Test]
		public void Returns_Fail_For_Empty_Response()
		{
			var verdict = llmaid.JudgeProcessor.ParseVerdict(string.Empty);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(1);
		}

		[Test]
		public void Returns_Fail_For_Whitespace_Only_Response()
		{
			var verdict = llmaid.JudgeProcessor.ParseVerdict("   \n\t  ");

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(1);
		}

		[Test]
		public void Ignores_Non_Bullet_Lines_When_Parsing_Violations()
		{
			var response = """
				FAIL
				The following violations were found:
				- Variable renamed without permission
				Some additional commentary.
				- Import removed
				""";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(2);
			verdict.Violations[0].ShouldBe("Variable renamed without permission");
			verdict.Violations[1].ShouldBe("Import removed");
		}

		// The following tests document the response-judge output format explicitly.
		// In response mode the judge LLM receives the original + new file (not a diff),
		// but produces the same PASS / FAIL verdict format — so ParseVerdict handles both.

		[Test]
		public void Returns_Pass_When_Response_Judge_Approves_Documentation_Only_Changes()
		{
			// Simulates a response-judge PASS for a code-documenter run
			var verdict = llmaid.JudgeProcessor.ParseVerdict("PASS");

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
		}

		[Test]
		public void Returns_Fail_When_Response_Judge_Detects_Code_Change()
		{
			// Simulates a response-judge FAIL when an LLM sneaks in a code edit
			var response = """
				FAIL
				- Method `Calculate()` body was modified: `return x + 1;` changed to `return x + 2;`
				- Private field `_cache` was given a new XML doc comment (not permitted)
				""";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.Length.ShouldBe(2);
			verdict.Violations[0].ShouldBe("Method `Calculate()` body was modified: `return x + 1;` changed to `return x + 2;`");
			verdict.Violations[1].ShouldBe("Private field `_cache` was given a new XML doc comment (not permitted)");
		}

		[Test]
		public void Returns_Pass_When_Response_Judge_Contains_Thinking_Preamble_Before_PASS()
		{
			// Models with extended thinking may emit reasoning text before PASS
			var response = """
				Let me compare the original and new file carefully...
				The only changes are added XML doc comments on public members.
				No executable code was modified.
				PASS
				""";

			var verdict = llmaid.JudgeProcessor.ParseVerdict(response);

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
		}
	}

	public class JudgeVerdictRecord : JudgeProcessorTests
	{
		[Test]
		public void Pass_Factory_Returns_Passed_True_With_Empty_Violations()
		{
			var verdict = JudgeVerdict.Pass();

			verdict.Passed.ShouldBe(true);
			verdict.Violations.ShouldBeEmpty();
			verdict.Usage.ShouldBeNull();
		}

		[Test]
		public void Fail_Factory_Returns_Passed_False_With_Violations()
		{
			var violations = new[] { "violation 1", "violation 2" };
			var verdict = JudgeVerdict.Fail(violations);

			verdict.Passed.ShouldBe(false);
			verdict.Violations.ShouldBe(violations);
			verdict.Usage.ShouldBeNull();
		}
	}
}
