# LLMaid

LLMaid is a command-line tool designed to automate the process of AI supported file changes like code refactoring using large language models. It reads source code files, sends them to a the Ollama API, and writes back the models answers. The tool is highly configurable and supports every kind of text-based input file.

![image](https://github.com/user-attachments/assets/015ba09b-4ce5-439f-a6af-4e20da6e511e)

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- An Ollama instance

## Installation

```bash
git clone https://github.com/awaescher/llmaid
cd llmaid

dotnet restore

# edit appsettings.json to your needs
# edit systemprompt.txt to your needs

dotnet run --project llmaid
```


## Configuration

Change the `appsettings.json` file in the root directory to your needs:

```json
{
  "Uri": "https://localhost:11434",            // the Ollama API endpoint
  "Model": "deepseek-cover-v2:16b",            // the model to use
  "SourcePath": "./src",                       // the path to look for files to change
  "FilePatterns": [ "*.cs", "*.js" ],          // the file types to change
  "PromptFile": "./systemprompt.txt",         // the system prompt to prime the model
  "Temperature": 0.7,                          // the models temperature (0 precise to 1 creative)
  "WriteCodeToConsole": true,                  // whether or not the models response should be shown in the console
  "ReplaceFiles": true                         // whether or not the files should be replaced with the model's response
}
```

---

❤ Made with [Spectre.Console](https://github.com/spectreconsole/spectre.console) and [OllamaSharp](https://github.com/awaescher/OllamaSharp).
