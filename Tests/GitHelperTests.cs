using llmaid;
using Shouldly;

namespace Tests;

public class GitHelperTests
{
	public class IsInsideGitRepoAsyncMethod : GitHelperTests
	{
		[Test]
		public async Task Returns_True_For_Path_Inside_This_Repository()
		{
			// The test project itself lives inside the llmaid git repository
			var thisFile = Path.GetFullPath(typeof(GitHelperTests).Assembly.Location);

			var result = await GitHelper.IsInsideGitRepoAsync(thisFile);

			result.ShouldBe(true);
		}

		[Test]
		public async Task Returns_False_For_Temp_Directory_Outside_Any_Repo()
		{
			// Create a fresh temp directory that is definitely not a git repo
			var tempDir = Path.Combine(Path.GetTempPath(), $"llmaid-test-{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempDir);

			try
			{
				var result = await GitHelper.IsInsideGitRepoAsync(tempDir);

				result.ShouldBe(false);
			}
			finally
			{
				Directory.Delete(tempDir, recursive: true);
			}
		}

		[Test]
		public async Task Accepts_File_Path_As_Well_As_Directory_Path()
		{
			// Passing a file path should work the same as its parent directory
			var thisFile = Path.GetFullPath(typeof(GitHelperTests).Assembly.Location);
			var thisDir = Path.GetDirectoryName(thisFile)!;

			var resultForFile = await GitHelper.IsInsideGitRepoAsync(thisFile);
			var resultForDir = await GitHelper.IsInsideGitRepoAsync(thisDir);

			resultForFile.ShouldBe(resultForDir);
		}
	}
}
