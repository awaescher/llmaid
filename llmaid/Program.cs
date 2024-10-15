using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;

namespace llmaid;

internal class Program
{
    static async Task Main(string[] args)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        // Aufbau der Konfiguration
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .Build();

        // Bindung der Konfiguration an die Arguments-Klasse
        var arguments = config.GetSection("Arguments").Get<Arguments>();

        // Validierung der Argumente
        arguments.Validate();

        var systemPromptTemplate = await File.ReadAllTextAsync(arguments.PromptFile, cancellationToken);

        var loader = new FileLoader();

        var ollama = new OllamaApiClient(arguments.OllamaUri, arguments.Model);

        foreach (var file in loader.Get(arguments.SourcePath, arguments.FilePatterns))
        {
            Console.WriteLine($"Processing {file} ...");

            var code = await File.ReadAllTextAsync(file, cancellationToken);
            var systemPrompt = systemPromptTemplate
                .Replace("%CODE%", code)
                .Replace("%CODELANGUAGE%", GetCodeLanguageByFileExtension(Path.GetExtension(file)))
                .Replace("%FILENAME%", Path.GetFileName(file));

            var userPrompt = """
%FILENAME%
``` %CODELANGUAGE%
%CODE%
```
""";
            userPrompt = userPrompt
                .Replace("%CODE%", code)
                .Replace("%CODELANGUAGE%", GetCodeLanguageByFileExtension(Path.GetExtension(file)))
                .Replace("%FILENAME%", Path.GetFileName(file));

            var chat = new Chat(ollama);
            chat.Messages.Add(new Message { Role = ChatRole.System, Content = systemPrompt });

            var originalCodeLength = code.Length;
            var builder = new StringBuilder();

            var response = await chat.Send(userPrompt, cancellationToken).StreamToEnd(token => 
            { 
                builder.Append(token);
                UpdateProgress(originalCodeLength, builder.Length);
            });

            if (arguments.DryRunToConsole)
            {
                Console.WriteLine(response);
            }
            else
            {
                var extractedCode = ExtractCode(response);
                await File.WriteAllTextAsync(file, extractedCode, cancellationToken);
            }
        }

        Console.WriteLine("Finished.");
    }

    private static void UpdateProgress(int originalCodeLength, int generatedCodeLength)
    {
        var percentage = 0.0d;

        if (originalCodeLength > 0 && generatedCodeLength > 0)
            percentage = ((double)generatedCodeLength / (double)originalCodeLength) * 100;

        percentage = Math.Min(100, Math.Max(0, percentage));

        Console.WriteLine($"{percentage:0}%");
    }

    private static string ExtractCode(string text)
    {
        string pattern = @"\`\`\`\s?(?:\w+)?\s*([\s\S]*?)\`\`\`";

        foreach (Match match in Regex.Matches(text, pattern).Where(m => m.Groups.Count > 1))
            return match.Groups[1].Value.Trim();

        return "";
    }

    private static string GetCodeLanguageByFileExtension(string fileExtension)
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
            ".vb" => "visualbasic",
            ".md" => "markdown",
            _ => ""
        };
    }

    public class FileLoader : IFileLoader
    {
        public IEnumerable<string> Get(string path, string[] searchPatterns)
        {
            foreach (var pattern in searchPatterns)
            {
                foreach (var entry in Directory.GetFileSystemEntries(path, pattern, SearchOption.AllDirectories))
                {
                    yield return entry;
                }
            }
        }
    }

    public class Arguments
    {
        public Uri OllamaUri { get; set; }

        public string Model { get; set; }

        public string SourcePath { get; set; }

        public string[] FilePatterns { get; set; } = [];

        public string PromptFile { get; set; }

        public bool DryRunToConsole { get; set; } = true;

        public void Validate()
        {
            if (string.IsNullOrEmpty(OllamaUri?.AbsolutePath))
                throw new ArgumentException("OllamaUri has to be defined.");

            if (FilePatterns?.Any() == false)
                throw new ArgumentException("At least one file pattern must be defined.");

            if (string.IsNullOrEmpty(SourcePath))
                throw new ArgumentException("Source path has to be defined.");

            if (string.IsNullOrEmpty(Model))
                throw new ArgumentException("Model has to be defined.");

            if (string.IsNullOrWhiteSpace(PromptFile) || !File.Exists(PromptFile))
                throw new FileNotFoundException($"Prompt file '{PromptFile}' does not exist.");
        }
    }
}