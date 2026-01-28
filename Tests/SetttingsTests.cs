using FluentAssertions;
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

			s1.Provider.Should().Be("ollama");
			s1.ApiKey.Should().Be("key2");
			s1.Uri.Should().Be(new Uri("http://example2.com"));
			s1.Model.Should().Be("model2");
			s1.TargetPath.Should().Be("path2");
			s1.Files.Should().Be(s2.Files);
			s1.Profile.Should().Be("file2");
			s1.WriteResponseToConsole.Should().Be(false);
			s1.ApplyCodeblock.Should().Be(true);
			s1.AssistantStarter.Should().Be("starter2");
			s1.Temperature.Should().Be(0.7f);
			s1.SystemPrompt.Should().Be("prompt2");
			s1.MaxRetries.Should().Be(2);
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

		s1.Provider.Should().Be("openai");
		s1.ApiKey.Should().Be("key1");
		s1.Uri.Should().Be(new Uri("http://example.com"));
		s1.Model.Should().Be("model1");
		s1.TargetPath.Should().Be("path1");
		s1.Files.Should().Be(s1.Files);
		s1.Profile.Should().Be("file1");
		s1.WriteResponseToConsole.Should().Be(true);
		s1.ApplyCodeblock.Should().Be(false);
		s1.AssistantStarter.Should().Be("starter1");
		s1.Temperature.Should().Be(0.5f);
		s1.SystemPrompt.Should().Be("prompt1");
		s1.MaxRetries.Should().Be(1);
	}
}
