namespace llmaid;

/// <summary>
/// Parses a YAML profile file into a <see cref="Settings"/> instance.
/// </summary>
internal static class ProfileParser
{
	internal static async Task<Settings> ParseAsync(string profileFile)
	{
		if (!File.Exists(profileFile))
			throw new FileNotFoundException($"Profile file '{profileFile}' does not exist.");

		var content = await File.ReadAllTextAsync(profileFile).ConfigureAwait(false);

		var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
			.WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build();

		return deserializer.Deserialize<Settings>(content) ?? Settings.Empty;
	}
}
