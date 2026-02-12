# AGENTS.md - Coding Agent Guidelines for llmaid

## Project Overview

llmaid is a C# .NET 10.0 command-line tool that automates AI-supported file changes using large language models. It reads source code files, sends them to Ollama, LM Studio, or OpenAI-compatible APIs, and writes back the model's responses.

## Build, Run, Test, and Lint Commands

### Build
```bash
dotnet build                           # Build entire solution
dotnet build llmaid/llmaid.csproj      # Build main project only
```

### Run
```bash
dotnet run --project llmaid -- --profile ./profiles/code-documenter.yaml
```

### Testing the app (CLI) locally

Choose the test profile you want to test, like `./profiles/code-documenter.yaml`
Use the following commands to test llmaid locally against `./testfiles`. Successful runs will show Git changes in the testfiles directory.
Using `./testfiles` will change all the files in that directory which is good to check the results but takes long. You can also pick single files from there and use these as targetPath to speed up the test.

**Using LM Studio (preferred):**
```bash
dotnet run --project llmaid -- --profile TEST-PROFILE-HERE --targetPath ./testfiles --provider lmstudio --uri http://localhost:1234/v1 --model openai-gpt-oss-120b --verbose
```

If the model is not available, query the models endpoint to find an available model:
```bash
curl http://localhost:1234/api/v1/models
```

**Using Ollama (fallback):**
```bash
dotnet run --project llmaid -- --profile TEST-PROFILE-HERE --targetPath ./testfiles --provider ollama --uri http://localhost:11434 --model gpt-oss:120b --verbose
```

Query available models via:
```bash
curl http://localhost:11434/api/tags
```

### Test
```bash
# Run all tests
dotnet test

# Run all tests with verbose output
dotnet test --verbosity normal

# Run a single test by method name (partial match)
dotnet test --filter "Name=Ignores_Text_Outside_Of_The_Code_Block"

# Run a single test by fully qualified name
dotnet test --filter "FullyQualifiedName=Tests.CodeBlockExtractorTests.ExtractMethod.Ignores_Text_Outside_Of_The_Code_Block"

# Run all tests in a class
dotnet test --filter "ClassName=Tests.CodeBlockExtractorTests"

# Run tests matching a pattern
dotnet test --filter "Name~Accepts_"
```

### Lint and Format
```bash
dotnet format                          # Auto-fix formatting issues
dotnet format --verify-no-changes      # Check formatting without fixing
```

## Project Structure

```
llmaid/
├── llmaid/                 # Main application source code
│   ├── Program.cs          # Entry point, CLI parsing, file processing
│   ├── Settings.cs         # Configuration model with validation
│   ├── FileLoader.cs       # Glob pattern matching for file discovery
│   ├── CodeBlockExtractor.cs # Extracts code blocks from LLM responses
│   └── appsettings.json    # Base configuration (provider, URI, API key)
├── Tests/                  # NUnit test project
├── profiles/               # YAML profile files for different LLM tasks
└── testfiles/              # Sample files for testing
```

## Code Style Guidelines

### Indentation and Whitespace
- **Indentation**: Use tabs, not spaces
- **Indent size**: 4 for code files, 2 for XML/JSON/YAML
- **Line endings**: CRLF
- **Trailing whitespace**: Trim in code files
- **Charset**: UTF-8 for code files

### Namespaces
- **File-scoped namespaces required** (warning level)
```csharp
// Correct
namespace llmaid;

// Incorrect
namespace llmaid
{
}
```

### Braces and Formatting (Allman Style)
- New line before opening brace for all constructs
- New line before `else`, `catch`, `finally`
- Space after keywords in control flow statements
- Space around binary operators

### Type Declarations
- **Prefer `var`** for all local variables
- **Use language keywords** (`string`, `int`, `bool`) not framework types (`String`, `Int32`, `Boolean`)

### Naming Conventions

| Symbol Type | Convention | Example |
|-------------|------------|---------|
| Constants | `UPPER_CASE` | `private const int MAX_RETRIES = 3;` |
| Private fields | `_camelCase` | `private string _assistantStarter;` |
| Public properties | `PascalCase` | `public string Provider { get; set; }` |
| Methods | `PascalCase` | `public void ProcessFile()` |
| Parameters | `camelCase` | `void Method(string fileName)` |
| Local variables | `camelCase` | `var fileCount = 0;` |
| Interfaces | `IPascalCase` | `public interface IFileLoader` |
| Classes/Structs/Enums | `PascalCase` | `public class Settings` |

### Imports (Using Directives)
- Sort `System.*` namespaces first
- Avoid `this.` qualification
- Remove unnecessary usings (IDE0005 warning)

```csharp
// Correct order
using System.ClientModel;
using System.CommandLine;
using System.Text;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Spectre.Console;
```

### Expression Bodies
- **Methods/Constructors**: Prefer block bodies
- **Properties/Accessors**: Prefer expression bodies

### Nullable Reference Types
- Nullable is enabled project-wide
- Use `?` suffix for nullable types: `string?`, `int?`
- Use null-coalescing (`??`) and null-propagation (`?.`)

### Error Handling
- Throw `ArgumentException` for invalid arguments
- Throw `FileNotFoundException` for missing files
- Use `try-finally` for resource cleanup
- Async methods should propagate exceptions naturally

```csharp
if (string.IsNullOrEmpty(Uri?.AbsolutePath))
    throw new ArgumentException("Uri has to be defined.");

if (!File.Exists(profileFile))
    throw new FileNotFoundException($"Profile file '{profileFile}' does not exist.");
```

### Async/Await
- Mark async methods with `Async` suffix where appropriate
- Always await async calls (CS4014 is an error)
- Use `ConfigureAwait(false)` in library code

## Testing Conventions

### Framework
- **NUnit 3.14** for test framework
- **Shouldly** for assertions
- Global using for `NUnit.Framework` in test project

### Test Organization
- Use nested classes to group related tests
- Parent class name = class under test
- Nested class name = method under test

```csharp
public class CodeBlockExtractorTests
{
    public class ExtractMethod : CodeBlockExtractorTests
    {
        [Test]
        public void Ignores_Text_Outside_Of_The_Code_Block()
        {
            // Arrange, Act, Assert
        }
    }
}
```

### Test Naming
- Use underscores to separate words: `Accepts_Xml_Code_Block`
- Describe the expected behavior
- Use `[Ignore("reason")]` for known failing tests

### Assertions
- Use Shouldly: `.ShouldBe()`, `.ShouldStartWith()`
```csharp
code.ShouldBe("expected value");
result.ShouldStartWith("prefix");
```
