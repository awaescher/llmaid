# Large Language Maid

llmaid is a command-line tool designed to automate the process of AI supported file changes like code refactoring using large language models. It reads source code files, sends them to a Ollama or an OpenAI-compatible API, and writes back the models answers. The tool is highly configurable and supports every kind of text-based input file.

![image](https://github.com/user-attachments/assets/015ba09b-4ce5-439f-a6af-4e20da6e511e)

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
