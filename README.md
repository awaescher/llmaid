# Large Language Maid

llmaid is a command-line tool designed to automate the process of AI supported file changes using large language models. It reads source code files, sends them to Ollama, LM Studio, or any OpenAI-compatible API, and writes back the models answers. The tool is highly configurable and supports every kind of text-based input file.

> [!NOTE]
> 1. Paid services such as ChatGPT can cause high API costs if they are used with many files. Double check your jobs.
> 2. You may get lower quality when using local models with [Ollama](https://ollama.com) or [LM Studio](https://lmstudio.ai), but it's completely free and your files will never leave your computer.

![image](https://github.com/user-attachments/assets/015ba09b-4ce5-439f-a6af-4e20da6e511e)

## What can it do?

llmaid will run through every file in a path you specify and rewrite, analyse or summarize it. Pretty much everything you can come up with, as long as you can write a good system prompt.

> [!NOTE]
> The quality mainly depends on the model you want to use. ChatGPT works great, but I have also had good results with medium sized local models like `mistral-small:22b` and `qwen2.5:32b`.

This repository provides a [few profile files](/profiles), for example:

### Documenting code
With this prompt, llmaid will scan and rewrite each code file and generate missing summaries, fix typos, remove wrong comments and much more:

![Code documenter](./docs/document%20code.png)

### Finding unprofessional slang
This prompt will output one json code block for each file. There it lists findings such as insults, cringe comments, and much more including a severity rating and a description what it thinks about the things it found:
![Review files](./docs/review%20files.png)

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- An Ollama instance, LM Studio, or an OpenAI-compatible API including api key

## Installation

```bash
git clone https://github.com/awaescher/llmaid
cd llmaid

# edit ./llmaid/appsettings.json for provider settings
# edit or create a profile in ./profiles/ for task-specific settings

dotnet run --project llmaid
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
  "OllamaMinNumCtx": 64000 // Minimum context length for the Ollama provider to prevent unnecessary model reloads (default 20480)
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
cooldownSeconds: 5  # wait 5 seconds between files to prevent overheating

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
# Use a specific profile with dotnet run
dotnet run --project llmaid -- --profile ./profiles/code-documenter.yaml

# Use a specific profile (when running the compiled binary)
llmaid --profile ./profiles/sql-injection-changer.yaml

# Run without profile (all settings via CLI)
llmaid --provider openai --model gpt-4o --targetPath ./src --systemPrompt "..."

# Dry run to see which files would be processed
llmaid -- --profile ./profiles/code-documenter.yaml --dryRun

# Verbose output with detailed token and timing information
llmaid -- --profile ./profiles/code-documenter.yaml --verbose
```

Available arguments:
- `--provider` – ollama, openai, lmstudio, openai-compatible
- `--uri` – API endpoint URL
- `--apiKey` – API key (if required)
- `--model` – Model identifier
- `--profile` – Path to YAML profile
- `--targetPath` – Directory with files to process
- `--applyCodeblock` – `true` extracts codeblock and overwrites file, `false` outputs response to console
- `--temperature` – Model temperature (0-2)
- `--systemPrompt` – System prompt text
- `--dryRun` – Simulate without changes
- `--maxRetries` – Retry count on failures
- `--verbose` – Show detailed output (tokens, timing, settings)
- `--cooldownSeconds` – Cooldown time in seconds after processing each file (default: 0, prevents processor overheating)

### Supported Providers

| Provider | URI | API Key Required |
|----------|-----|------------------|
| `ollama` | `http://localhost:11434` | No |
| `lmstudio` | `http://localhost:1234/v1` | No (use empty string or any placeholder) |
| `openai` | `https://api.openai.com/v1` | Yes |
| `openai-compatible` | Your server's URL | Depends on server |

## FAQ

### I get an 404 (Not Found)
It is very likely that Ollama returns this 404 because it doesn't know the model that's specified in the appsettings.json. Make sure to specify a model you have downloaded.

---

❤ Made with [Spectre.Console](https://github.com/spectreconsole/spectre.console) and [OllamaSharp](https://github.com/awaescher/OllamaSharp).
