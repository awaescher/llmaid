using Shouldly;
using llmaid;

namespace Tests;

public class SettingsTests
{
	public class OverrideWithMethod : SettingsTests
	{
		[Test]
		public void Overwrites_Provided_Values()
		{
			var s1 = new Settings
			{
				Provider = "openai",
				ApiKey = "key1",
				Uri = new Uri("http://example.com"),
				Model = "model1",
				TargetPath = "path1",
				Files = new Files([], []),
				Profile = "file1",
				WriteResponseToConsole = true,
				ApplyCodeblock = false,
				AssistantStarter = "starter1",
				Temperature = 0.5f,
				SystemPrompt = "prompt1",
				MaxRetries = 1
			};

			var s2 = new Settings
			{
				Provider = "ollama",
				ApiKey = "key2",
				Uri = new Uri("http://example2.com"),
				Model = "model2",
				TargetPath = "path2",
				Files = new Files([], []),
				Profile = "file2",
				WriteResponseToConsole = false,
				ApplyCodeblock = true,
				AssistantStarter = "starter2",
				Temperature = 0.7f,
				SystemPrompt = "prompt2",
				MaxRetries = 2
			};

			s1.OverrideWith(s2);

			s1.Provider.ShouldBe("ollama");
			s1.ApiKey.ShouldBe("key2");
			s1.Uri.ShouldBe(new Uri("http://example2.com"));
			s1.Model.ShouldBe("model2");
			s1.TargetPath.ShouldBe("path2");
			s1.Files.ShouldBe(s2.Files);
			s1.Profile.ShouldBe("file2");
			s1.WriteResponseToConsole.ShouldBe(false);
			s1.ApplyCodeblock.ShouldBe(true);
			s1.AssistantStarter.ShouldBe("starter2");
			s1.Temperature.ShouldBe(0.7f);
			s1.SystemPrompt.ShouldBe("prompt2");
			s1.MaxRetries.ShouldBe(2);
		}
	}


	[Test]
	public void Does_Not_Overwrite_With_Default_Values()
	{
		var s1 = new Settings
		{
			Provider = "openai",
			ApiKey = "key1",
			Uri = new Uri("http://example.com"),
			Model = "model1",
			TargetPath = "path1",
			Files = new Files([], []),
			Profile = "file1",
			WriteResponseToConsole = true,
			ApplyCodeblock = false,
			AssistantStarter = "starter1",
			Temperature = 0.5f,
			SystemPrompt = "prompt1",
			MaxRetries = 1
		};

		var s2 = new Settings();
		s1.OverrideWith(s2);

		s1.Provider.ShouldBe("openai");
		s1.ApiKey.ShouldBe("key1");
		s1.Uri.ShouldBe(new Uri("http://example.com"));
		s1.Model.ShouldBe("model1");
		s1.TargetPath.ShouldBe("path1");
		s1.Files.ShouldBe(s1.Files);
		s1.Profile.ShouldBe("file1");
		s1.WriteResponseToConsole.ShouldBe(true);
		s1.ApplyCodeblock.ShouldBe(false);
		s1.AssistantStarter.ShouldBe("starter1");
		s1.Temperature.ShouldBe(0.5f);
		s1.SystemPrompt.ShouldBe("prompt1");
		s1.MaxRetries.ShouldBe(1);
	}
}
