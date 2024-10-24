# Large Language Maid

llmaid is a command-line tool designed to automate the process of AI supported file changes using large language models. It reads source code files, sends them to a Ollama or an OpenAI-compatible API, and writes back the models answers. The tool is highly configurable and supports every kind of text-based input file.

> [!NOTE]
> 1. Paid services such as ChatGPT can cause high API costs if they are used with many files. Double check your jobs.
> 2. You may get lower quality when using local models with [Ollama](https://ollama.com), but it's completely free and your files will never leave your computer.

![image](https://github.com/user-attachments/assets/015ba09b-4ce5-439f-a6af-4e20da6e511e)

## What can it do?

llmaid will run through every file in a path you specify and rewrite, analyse or summarize it. Pretty much everything you can come up with, as long as you can write a good system prompt.

> [!NOTE]
> The quality mainly depends on the model you want to use. ChatGPT works great, but I have also had good results with medium sized local models like `mistral-small:22b` and `qwen2.5:32b`.

This repository provides a [few system prompts](/prompts), for example:

### Documenting code
With this prompt, llmaid will scan and rewrite each code file and generate missing summaries, fix typos, remove wrong comments and much more:

![Code documenter](./docs/document%20code.png)

### Finding unprofessional slang
This prompt will output one json code block for each file. There it lists findings such as insults, cringe comments, and much more including a severity rating and a description what it thinks about the things it found:
![Review files](./docs/review%20files.png)

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- An Ollama instance or an OpenAI-compatible API including api key

## Installation

```bash
git clone https://github.com/awaescher/llmaid
cd llmaid

# edit ./llmaid/appsettings.json to your needs
# edit ./systemprompt.txt to your needs

dotnet run --project llmaid
```

## Configuration

Change the `appsettings.json` file in the root directory to your needs:

```json
{
  "Provider": "ollama",                        // ollama or openai (works with any compatible api)
  "Uri": "https://localhost:11434",            // Ollama or OpenAI (compatible) endpoints like http://localhost:11434 or https://api.openai.com
  "ApiKey": ""                                 // not required for Ollama
  "Model": "deepseek-coder-v2:16b",            // the model to use
  "PromptFile": "./systemprompt.txt",          // the system prompt to prime the model
  "SourcePath": "./testcode",                  // the path to look for files to change
  "FilePatterns": [ "*.cs", "*.js" ],          // the file types to change
  "Temperature": 0.7,                          // the models temperature (0 precise to 1 creative)
  "WriteCodeToConsole": true,                  // whether or not the models response should be shown in the console
  "ReplaceFiles": true                         // whether or not the files should be replaced with the model's response
}
```

## FAQ

### I get an 404 (Not Found)
It is very likely that Ollama returns this 404 because it doesn't know the model that's specified in the appsettings.json. Make sure to specify a model you have downloaded.

---

❤ Made with [Spectre.Console](https://github.com/spectreconsole/spectre.console) and [OllamaSharp](https://github.com/awaescher/OllamaSharp).
