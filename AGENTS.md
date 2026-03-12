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

Each profile in `./profiles/` has a dedicated folder under `./testfiles/` with matching demo files.
Use the commands below to run a specific profile against its testfiles. Successful runs may show Git changes in the testfiles directory.

The main developer is using LM Studio and the LLM `qwen3.5-35b-a3b` these days. Testing the code-documenter profile should look like this.

```bash
dotnet run --project llmaid -- --profile ./profiles/code-documenter.yaml --targetPath "./testfiles/code" --provider lmstudio --uri http://localhost:1234/v1 --model mlx-community/qwen3.5-35b-a3b
```

The code-documenter profile is changing files (`applyCodeblock=true`) so in addition to checking the console output, watch expected changes with git diff.

**Using LM Studio (preferred):**
```bash
dotnet run --project llmaid -- --profile TEST-PROFILE-HERE --targetPath TESTFILES-FOLDER-HERE --provider lmstudio --uri http://localhost:1234/v1 --model MODEL-HERE --verbose
```

If the model is not available, query the models endpoint to find an available model:
```bash
curl http://localhost:1234/api/v1/models
```

**Using Ollama (fallback):**
```bash
dotnet run --project llmaid -- --profile TEST-PROFILE-HERE --targetPath TESTFILES-FOLDER-HERE --provider ollama --uri http://localhost:11434 --model MODEL-HERE --verbose
```

Query available models via:
```bash
curl http://localhost:11434/api/tags
```

#### Profile smoke tests

The table below lists the expected behavior for each profile when run against the provided demo files.
Use this to verify a profile works correctly after changes. `applyCodeblock: true` profiles write changes back to files (visible as Git diffs); `applyCodeblock: false` profiles print structured output to the console.

| Profile | Testfiles folder | applyCodeblock | Expected behavior |
|---------|-----------------|:-:|---|
| `code-documenter.yaml` | `./testfiles/code` | ✅ | Adds/fixes XML/JSDoc/docstring documentation on public members in all code files |
| `code-changer.yaml` | `./testfiles/code` | ✅ | Replaces `new List<T> {}` / `new T[] {}` with C# 12 collection expressions `[...]` |
| `code-changer-vb.yaml` | `./testfiles/code` | ✅ | Replaces `And`→`AndAlso` and `Or`→`OrElse` in VB.NET files (only without method calls) |
| `unprofessional-content-finder.yaml` | `./testfiles/code` | 📋 | Returns JSON findings for `cache.php` ("I freaking hate PHP"), `contract.sol` ("f*ckface", "libido"), `FileReader.cs` ("Wurstblinker"), `JumpWidget.cpp` ("kurva"); all others return `OK` |
| `unprofessional-content-fixer.yaml` | `./testfiles/code` | ✅ | Removes/replaces profanity and cringe comments; leaves clean files unchanged |
| `sensitive-data-marker.yaml` | `./testfiles/code` | ✅ | Adds `// TODO security review: sensitive data` comment to `JumpWidget.cpp` ("pa55wood!123") |
| `sql-injection-changer.yaml` | `./testfiles/code` | ✅ | Wraps unsafe SQL string concatenations with `SqlTools.MakeSqlValue()` in `SqlInjectionTests.vb` |
| `age-rater.yaml` | `./testfiles/age-rater` | 📋 | Returns YAML age ratings; `story-fsk0.txt` → FSK 0, `story-fsk12.txt` → FSK 12; images rated by visible content |
| `wiki-proofreader.yaml` | `./testfiles/code` | ✅ | Fixes spelling/grammar in `.md` and `.txt` files; leaves code blocks unchanged |
| `nda-checker.yaml` | *(provide an NDA text file)* | 📋 | Returns ✅/❌/⚠️ for each of the 15 company NDA rules with quoted evidence |
| `invoice-checker.yaml` | `./testfiles/invoice-checker` | 📋 | `invoice-correct.txt` → all 12 §14 UStG fields pass + calculations correct; `invoice-problematic.txt` → multiple ❌ (missing VAT ID, address, delivery date, bank details, rounding error) |
| `brand-detector.yaml` | `./testfiles/brand-detector` | 📋 | Returns YAML listing all visible brand logos/wordmarks per image with confidence and location |
| `meme-analyzer.yaml` | `./testfiles/meme-analyzer` | 📋 | Returns YAML per image with tone, content flags, and corporate suitability ratings for internal/external/customer-facing use |
| `image-alt-text-generator.yaml` | `./testfiles/alt-text-generator` | 📋 | Returns YAML with three alt text variants (short ≤125 chars, medium, long) plus visible text transcription per image |

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
├── llmaid/                     # Main application source code
│   ├── Program.cs              # Entry point, CLI parsing, file processing
│   ├── Settings.cs             # Configuration model with validation
│   ├── FileLoader.cs           # Glob pattern matching for file discovery
│   ├── CodeBlockExtractor.cs   # Extracts code blocks from LLM responses
│   └── appsettings.json        # Base configuration (provider, URI, API key)
├── Tests/                      # NUnit test project
├── profiles/                   # YAML profile files for different LLM tasks
└── testfiles/                  # Demo files for testing profiles
    ├── code/                   # Source code files (C#, VB, JS, TS, PHP, ...)
    ├── age-rater/              # Text stories and images for age rating tests
    ├── invoice-checker/        # Invoice text files for §14 UStG compliance tests
    ├── brand-detector/         # Images with brand logos for detection tests
    ├── meme-analyzer/          # Meme images for tone and suitability tests
    └── alt-text-generator/     # Photos and screenshots for alt text generation
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
