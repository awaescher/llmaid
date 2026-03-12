# Large Language Maid

Throw files against LLMs.

llmaid is a command-line tool designed to automate the process of AI supported file changes using large language models. It reads source code files, sends them to Ollama, LM Studio, or any OpenAI-compatible API, and writes back the models answers. The tool is highly configurable and supports every kind of text-based input file.

---

### 💬 But there is GitHub Copilot, Cursor, Claude Code, OpenCode and so many more. Isn't llmaid outdated?

Yes, very. But it serves a slightly different use-case.

These partly autonomous agents are amazing but they cannot be used to fix typos or add documentation in every single code file of a repository. These tools work in a different way and need to minimize their context while navigating through your codebase.

llmaid is different: Every file is a new conversation. While there is no autonomous intelligence, it can review or edit every file in total based on your instructions. This is handy to find things in your codebase you could not search with RegEx, for example. The feature of writing the LLM response back also enables batch-processing of every single file in the codebase, like "fix all typos".

---

> [!NOTE]
> 1. Paid services such as ChatGPT can cause high API costs if they are used with many files. Double check your config.
> 2. You may get lower quality when using local models with [Ollama](https://ollama.com) or [LM Studio](https://lmstudio.ai), but it's completely free and your files will never leave your computer.

![image](https://github.com/user-attachments/assets/015ba09b-4ce5-439f-a6af-4e20da6e511e)

## Installation

### Homebrew (macOS / Linux)

```bash
brew install awaescher/tap/llmaid
```

### Manual Download

Download the latest binary for your platform from [GitHub Releases](https://github.com/awaescher/llmaid/releases/latest).

## Building from Source

If you want to build llmaid yourself, you need the [.NET 10.0 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project llmaid -- --profile ./profiles/code-documenter.yaml --targetPath ./testfiles/code
```

## What can it do?

llmaid will run through every file in a path you specify and rewrite, analyse or summarize it. Pretty much everything you can come up with, as long as you can write a good system prompt.

This repository provides a [few profile files](/profiles), for example:

### Documenting code
With this prompt, llmaid will scan and rewrite each code file and generate missing summaries, fix typos, remove wrong comments and much more:

![Code documenter](./docs/document%20code.png)

### Finding unprofessional slang
This prompt will output one json code block for each file. There it lists findings such as insults, cringe comments, and much more including a severity rating and a description what it thinks about the things it found:
![Review files](./docs/review%20files.png)

## Profiles & Examples

Each profile has demo files in `./testfiles/` you can run right away. Replace `MODEL-HERE` with a model available on your LLM provider.

### Code profiles

**Document public members in source code** — rewrites files with XML/JSDoc/docstring comments:
```bash
llmaid --profile ./profiles/code-documenter.yaml --targetPath ./testfiles/code
```

**Find unprofessional language in code** — outputs a JSON report of findings per file:
```bash
llmaid --profile ./profiles/unprofessional-content-finder.yaml --targetPath ./testfiles/code
```

**Fix unprofessional language in code** — rewrites files with neutralized comments:
```bash
llmaid --profile ./profiles/unprofessional-content-fixer.yaml --targetPath ./testfiles/code
```

### Image profiles

**Rate content by age classification** — outputs YAML with FSK/USK/PEGI/ESRB ratings for text and images:
```bash
llmaid --profile ./profiles/age-rater.yaml --targetPath ./testfiles/age-rater
```

**Detect brand logos in images** — outputs YAML listing all visible brands with confidence and location:
```bash
llmaid --profile ./profiles/brand-detector.yaml --targetPath ./testfiles/brand-detector
```

**Generate alt text for images** — outputs YAML with three detail levels (short ≤125 chars, medium, long):
```bash
llmaid --profile ./profiles/image-alt-text-generator.yaml --targetPath ./testfiles/alt-text-generator
```

## Configuration

llmaid uses a **layered configuration system** where each layer can override the previous:

1. **appsettings.json** – Connection settings only (provider, URI, API key)
2. **Profile file (.yaml)** – Complete task configuration (model, paths, files, system prompt)
3. **Command line arguments** – Runtime overrides (highest priority)

This means you can prepare self-contained profiles for different tasks and still override individual settings via CLI.

### appsettings.json (Connection Settings)

This file only contains your LLM provider connection settings:

```json
{
  "Provider": "lmstudio",
  "Uri": "http://localhost:1234/v1",
  "ApiKey": "",
  "WriteResponseToConsole": true,
  "CooldownSeconds": 0,
  "MaxFileTokens": 102400,
  "OllamaMinNumCtx": 24000 // Minimum context length for the Ollama provider to prevent unnecessary model reloads (default 20480)
}
```

### Profile Files (.yaml)

Profiles are **self-contained task definitions** that include everything needed to run a specific job: model, target path, file patterns, and system prompt.

```yaml
# profiles/code-documenter.yaml

model: deepseek-coder-v2:16b
targetPath: ./src
temperature: 0.25
applyCodeblock: true
maxRetries: 1

files:
  include:
    - "**/*.{cs,vb,js,ts}"
  exclude:
    - bin/
    - obj/

systemPrompt: |
  You are an AI documentation assistant.
  The user will provide a code snippet. Review and improve its documentation:
  
  - Add missing summaries for public members
  - Fix typos in comments
  - Do NOT change the executable code
  
  Return the entire file in a markdown code block.
```

### Command Line Arguments

All settings can be overridden via CLI:

```bash
# Use a specific profile
llmaid --profile ./profiles/code-documenter.yaml

# Run without profile (all settings via CLI)
llmaid --provider openai --model gpt-4o --targetPath ./src --systemPrompt "..."

# Dry run to see which files would be processed
llmaid --profile ./profiles/code-documenter.yaml --dryRun

# Verbose output with detailed token and timing information
llmaid --profile ./profiles/code-documenter.yaml --verbose
```

Available arguments:
- `--provider` – ollama, openai, lmstudio, openai-compatible
- `--uri` – API endpoint URL
- `--apiKey` – API key (if required)
- `--model` – Model identifier
- `--profile` – Path to YAML profile
- `--targetPath` – Directory with files to process, or a single file (when specifying a file, glob patterns are ignored)
- `--applyCodeblock` – `true` extracts codeblock and overwrites file, `false` outputs response to console
- `--temperature` – Model temperature (0-2)
- `--systemPrompt` – System prompt text
- `--assistantStarter` – String to start the assistant's message (can guide model output format)
- `--dryRun` – Simulate without changes
- `--maxRetries` – Retry count on failures
- `--verbose` – Show detailed output (tokens, timing, settings)
- `--writeResponseToConsole` – Whether to write the model's response to the console (default: true)
- `--cooldownSeconds` – Cooldown time after processing each file to prevent overheating (default: 0)
- `--maxFileTokens` – Maximum tokens a file may contain before it is skipped (default: 102400)
- `--resumeAt` – Resume processing from a specific file (skips all files until a filename containing this pattern is found)
- `--ollamaMinNumCtx` – Minimum context length for Ollama provider to prevent unnecessary model reloads (default: 20480)
- `--preserveWhitespace` – Preserve original leading and trailing whitespace when writing files to avoid diff noise (default: false)
- `--showProgress` – Show progress indicator during file processing (default: true)
- `--reasoningTimeoutSeconds` – Maximum seconds a model may spend reasoning before the request is cancelled (default: 600, 0 = disabled)
- `--maxImageDimension` – Maximum image dimension in pixels, images are resized to fit while preserving aspect ratio (default: 2048)

### Supported Providers

| Provider | URI | API Key Required |
|----------|-----|------------------|
| `ollama` | `http://localhost:11434` | No |
| `lmstudio` | `http://localhost:1234/v1` | No (use empty string or any placeholder) |
| `openai` | `https://api.openai.com/v1` | Yes |
| `openai-compatible` | Your server's URL | Depends on server |


### System Prompt Placeholders

llmaid automatically replaces `{{PLACEHOLDER}}` tokens in the `systemPrompt` with live system values before sending the prompt to the model. This lets you write date-aware, locale-aware, or environment-aware prompts without hardcoding anything.

> [!NOTE]
> Placeholders are only replaced inside the `systemPrompt`. They are never applied to file contents or any other settings.

#### File (per-file, resolved for each processed file)

| Placeholder | Description |
|-------------|-------------|
| `{{CODE}}` | Full content of the current file being processed |
| `{{CODELANGUAGE}}` | Programming language derived from the file extension (e.g. `csharp`, `javascript`) |
| `{{FILENAME}}` | Name of the current file without its directory path |

#### Date & Time

| Placeholder | Example value | Description |
|-------------|---------------|-------------|
| `{{TODAY}}` | `2026-03-12` | Current date in ISO 8601 format |
| `{{NOW}}` | `2026-03-12T15:44:58` | Current date and time in ISO 8601 format |
| `{{YEAR}}` | `2026` | Current four-digit year |
| `{{MONTH}}` | `03` | Current month (01–12) |
| `{{WEEKDAY}}` | `Thursday` | Current day of the week (English) |

#### System & Environment

| Placeholder | Example value | Description |
|-------------|---------------|-------------|
| `{{USERNAME}}` | `awaescher` | OS login name of the current user |
| `{{MACHINENAME}}` | `my-macbook` | Network hostname of the machine |
| `{{TIMEZONE}}` | `Europe/Berlin` | IANA time zone of the local system |
| `{{NEWLINE}}` | _(platform newline)_ | Platform-specific line break (`\n` or `\r\n`) |

#### Locale & Formatting

| Placeholder | Example value (`de-DE`) | Example value (`en-US`) | Description |
|-------------|-------------------------|-------------------------|-------------|
| `{{CULTURE}}` | `de-DE` | `en-US` | BCP 47 locale tag of the current UI culture |
| `{{DATEFORMAT}}` | `dd.MM.yyyy` | `M/d/yyyy` | Short date pattern of the current culture |
| `{{TIMEFORMAT}}` | `HH:mm:ss` | `h:mm:ss tt` | Long time pattern of the current culture |
| `{{DATESEPARATOR}}` | `.` | `/` | Date separator character |
| `{{TIMESEPARATOR}}` | `:` | `:` | Time separator character |
| `{{DECIMALSEPARATOR}}` | `,` | `.` | Decimal separator character |
| `{{GROUPSEPARATOR}}` | `.` | `,` | Thousands group separator character |
| `{{CURRENCYSYMBOL}}` | `€` | `$` | Currency symbol |

#### Example usage

```yaml
systemPrompt: |
  Today is {{TODAY}} ({{WEEKDAY}}). The user's locale is {{CULTURE}}.
  Numbers use '{{DECIMALSEPARATOR}}' as decimal separator and '{{CURRENCYSYMBOL}}' as currency.
  Dates are formatted as {{DATEFORMAT}}.
  
  Analyze the provided invoice and check all calculations.
```

## FAQ

### Can I continue where I left off earlier?

Yes! Use the `--resumeAt` parameter with a pattern matching the filename where you want to resume. All files before that match will be skipped (like a dry run).

**Example:** If you interrupted llmaid after hundreds of files while it was processing `~/Developer/MyApp/UserService.cs`, you can continue like this`:

```bash
llmaid --profile ./profiles/code-documenter.yaml --resumeAt UserService
```

The pattern is case-insensitive and matches any part of the file path, so you don't need to specify the full path.

### I get an 404 (Not Found)
It is very likely that Ollama returns this 404 because it doesn't know the model that's specified in the appsettings.json. Make sure to specify a model you have downloaded.

---

❤ Made with [Spectre.Console](https://github.com/spectreconsole/spectre.console) and [OllamaSharp](https://github.com/awaescher/OllamaSharp).
