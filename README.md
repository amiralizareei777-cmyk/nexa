# Nexa — Intelligent Voice & Desktop AI Assistant for Windows

[![Target OS](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white)](https://microsoft.com/windows)
[![.NET Version](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-139%20Passed-brightgreen)](Nexa.Tests)

**Nexa** is a modern, high-performance, open-source AI desktop assistant engineered natively for **Windows 10 and Windows 11**. Built with C# and WPF on .NET 8, Nexa seamlessly integrates voice conversations, local wake-word detection, keyboard workflows, and OS-level automation with leading LLM providers (Google Gemini, OpenAI, Anthropic Claude, OpenRouter, OmniRoute, and custom OpenAI-compatible endpoints).

---

## Table of Contents

- [Key Features](#key-features)
- [System Requirements](#system-requirements)
- [Architecture & Tech Stack](#architecture--tech-stack)
- [Quick Start & Installation](#quick-start--installation)
  - [Prerequisites](#prerequisites)
  - [Clone & Build](#clone--build)
  - [Run the Application](#run-the-application)
  - [Creating a Standalone Executable](#creating-a-standalone-executable)
- [Initial Configuration](#initial-configuration)
  - [AI Provider Setup](#ai-provider-setup)
  - [Credential Security](#credential-security)
- [Usage Guide](#usage-guide)
  - [Keyboard Shortcuts](#keyboard-shortcuts)
  - [Voice Interaction & Wake Word](#voice-interaction--wake-word)
  - [Microphone Privacy Control](#microphone-privacy-control)
  - [Desktop Automation & System Actions](#desktop-automation--system-actions)
- [Solution Structure](#solution-structure)
- [Running Automated Tests](#running-automated-tests)
- [GitHub Deployment Guide](#github-deployment-guide)
- [License](#license)

---

## Key Features

- **Instant Access Palette**: Launch the floating assistant command bar from anywhere using the global hotkey (**`Alt + Space`**) or via the Windows System Tray.
- **Hands-Free Local Wake Word**: Native offline detection for the `"Nexa"` wake word using Windows Speech Recognition, paired with a spoken `"Yes"` acknowledgement and an ambient screen-edge glow.
- **Multi-Provider AI Architecture**:
  - **Google Gemini**: Full support for text and inline audio streaming (`WAV`).
  - **OpenAI & Compatible**: Supports GPT-4o / GPT-4o-mini with chat completions and native `input_audio`.
  - **Anthropic Claude**: Full text and tool-use support.
  - **OpenRouter**: Access hundreds of open-source and proprietary models with automatic Speech-to-Text preprocessing.
  - **OmniRoute & Local Models**: Connect to local LLMs (e.g., Ollama, LocalAI, vLLM, LM Studio) via OpenAI-compatible endpoints.
- **Enterprise-Grade Credential Security**: API keys are securely encrypted using the **Windows Credential Manager (WinCred / DPAPI)** rather than stored in plain-text config files.
- **Rich Desktop OS Automation**:
  - **App & File Launcher**: Launch Win32 programs, UWP apps, folders, documents, and web URLs.
  - **File Search**: Quickly discover files across local storage.
  - **Screenshot & Vision**: Instant screen captures for analysis or storage.
  - **File Generation**: Generate text, scripts, documents, and HTML code straight to `Desktop`, `Documents`, or `Downloads`, with instant opening in default editors or browsers.
  - **Audio Controls**: System volume adjustment and muting via CoreAudio.
  - **Guarded Shell Commands**: Safe command-line execution with mandatory user confirmation dialogs before running.
- **Modern Windows UX**: Dark and Light theme support, resizable window, F11 full-screen mode, system tray integration, and optional launch-on-startup.
- **Complete Privacy & Microphone Mute**: Single-click global microphone mute immediately disables wake-word listening, active voice sessions, and mic diagnostics while keeping text chat fully operational.

---

## System Requirements

- **Operating System**: Windows 10 (Build 17763 or higher) or Windows 11 (x64)
- **Runtime / SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (v8.0.400+ recommended)
- **Audio Hardware**: Working microphone and audio output device for voice features
- **Speech Engine**: Windows Speech Recognition components (standard on Windows 10/11)

---

## Architecture & Tech Stack

Nexa is organized into modular clean-architecture layers:

```
┌────────────────────────────────────────────────────────┐
│                        Nexa.App                        │
│          WPF UI / Command Palette / System Tray        │
└───────────────┬────────────────────────┬───────────────┘
                │                        │
┌───────────────▼───────────────┐ ┌──────▼───────────────┐
│         Nexa.Windows          │ │ Nexa.Infrastructure │
│  Win32 API, CoreAudio, Speech │ │ WinCred Store,       │
│  Wake Word, Shell Execution   │ │ Settings Repository  │
└───────────────┬───────────────┘ └──────┬───────────────┘
                │                        │
                └───────────────┬────────┘
                                │
┌───────────────────────────────▼────────────────────────┐
│                       Nexa.Core                        │
│      AI Provider Interfaces, Prompt Parsers, Models    │
└────────────────────────────────────────────────────────┘
```

- **Framework**: .NET 8 (C# 12)
- **UI Platform**: WPF (Windows Presentation Foundation) with XAML
- **Audio & Speech**: `NAudio`, `System.Speech`, Windows CoreAudio API
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`
- **Testing**: `xUnit`, `Moq`, FluentAssertions

---

## Quick Start & Installation

### Prerequisites

Ensure you have Git and .NET 8 SDK installed:

```powershell
# Verify .NET installation
dotnet --version
# Expected: 8.0.x
```

### Clone & Build

```powershell
# 1. Clone repository
git clone https://github.com/YOUR_USERNAME/nexa.git
cd nexa

# 2. Restore NuGet dependencies
dotnet restore Nexa.sln

# 3. Build the entire solution
dotnet build Nexa.sln -c Release
```

### Run the Application

You can launch Nexa directly from the .NET CLI:

```powershell
dotnet run --project Nexa.App
```

Or execute the compiled binary directly:

```powershell
.\Nexa.App\bin\Debug\net8.0-windows10.0.17763.0\Nexa.exe
```

### Creating a Standalone Executable

To produce a single-file, self-contained Windows executable that does not require .NET to be pre-installed on the host computer:

```powershell
dotnet publish Nexa.App\Nexa.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o .\publish
```

The resulting standalone executable will be located in the `.\publish\` folder as `Nexa.exe`.

---

## Initial Configuration

### AI Provider Setup

On first launch, Nexa opens its interactive setup guide:

1. Click on **Settings** (or right-click the system tray icon → **Settings**).
2. Select the **AI** tab.
3. Choose your preferred AI Provider:
   - **Google Gemini**: Get an API key from [Google AI Studio](https://aistudio.google.com/app/apikey).
   - **OpenAI**: Get an API key from the [OpenAI Platform](https://platform.openai.com/api-keys).
   - **Anthropic Claude**: Get an API key from [Anthropic Console](https://platform.claude.com/settings/keys).
   - **OpenRouter**: Get an API key from [OpenRouter Settings](https://openrouter.ai/settings/keys).
   - **OmniRoute / Custom**: Point to your local endpoint (e.g., `http://localhost:20128` or `http://localhost:11434/v1`).
4. Click **Refresh Models**, test the connection via **Test Connection**, and click **Save**.

### Credential Security

Nexa does not write your secret API keys to unencrypted JSON files. All credentials are saved into the **Windows Credential Manager** under isolated keys per provider, protected by Windows DPAPI.

---

## Usage Guide

### Keyboard Shortcuts

| Shortcut | Description |
| :--- | :--- |
| **`Alt + Space`** | Toggle the Nexa Command Palette from any application |
| **`Escape`** | Close / hide the palette to the taskbar/tray |
| **`F11`** | Toggle borderless Full-Screen mode |
| **`Enter`** | Send typed message / execute query |

### Voice Interaction & Wake Word

- **Wake Word**: Say `"Nexa"` out loud. Nexa will illuminate an ambient edge-glow border, respond with `"Yes"`, and start listening to your voice request.
- **Voice Capabilities**: Supports natural conversational flow, dictation, querying, and direct desktop task automation.

### Microphone Privacy Control

- Right-click the **Nexa System Tray Icon** and click **Mute Microphone** (or toggle it inside Settings).
- When muted, wake-word listening, background audio buffers, and voice sessions are immediately halted. Text-based interaction continues uninterrupted.

### Desktop Automation & System Actions

You can prompt Nexa in natural language. Nexa will automatically parse intent and execute actions:

```text
"Open Visual Studio Code"
"Set system volume to 40%"
"Search for invoice documents on my PC"
"Take a screenshot"
"Create a python script named backup.py on my desktop"
"Show my computer specs and memory usage"
```

*Note: For sensitive shell actions, Nexa will always prompt you with an interactive confirmation preview before executing.*

---

## Solution Structure

```text
nexa/
├── Nexa.sln                  # Main Visual Studio Solution
├── Nexa.App/                 # WPF Application, Views, ViewModels, System Tray
├── Nexa.Core/                # Domain models, AI abstractions, Intent routers
├── Nexa.Infrastructure/      # Windows Secret Store, Configuration services
├── Nexa.Windows/             # OS automation, Audio capture, Speech/Wake, Win32
├── Nexa.Tests/               # xUnit unit & integration test suites
├── docs/                     # Guides and HTML offline documentation
└── .gitignore                # Git ignore configuration for .NET / Visual Studio
```

---

## Running Automated Tests

Nexa includes an extensive test suite verifying AI providers, prompt parsing, safety filters, and action execution:

```powershell
dotnet test Nexa.Tests\Nexa.Tests.csproj
```

**Test Results**:
- 139 automated tests covering providers, action execution, security, and parsing.
- Automated tests mock physical hardware (microphone/speakers) for headless CI/CD compatibility.

---

## GitHub Deployment Guide

Follow these steps to initialize and push this project to your GitHub account:

### 1. Initialize Local Git Repository

```powershell
cd C:\Users\Amirali\Desktop\nexa

# Initialize repository
git init -b main

# Stage all project files (ignoring bin/obj via .gitignore)
git add .

# Create the initial commit
git commit -m "feat: Initial release of Nexa 1.0.0 desktop AI assistant"
```

### 2. Create Repository on GitHub

1. Go to [GitHub](https://github.com/new).
2. Set **Repository name** to `nexa` (or your preferred name).
3. Choose **Public** or **Private**.
4. **Do NOT** check "Add a README file", ".gitignore", or "license" (we already created them locally).
5. Click **Create repository**.

### 3. Link Remote & Push

```powershell
# Add remote origin (replace YOUR_USERNAME with your GitHub username)
git remote add origin https://github.com/YOUR_USERNAME/nexa.git

# Push code to main branch
git push -u origin main
```

---

## License

This project is licensed under the [MIT License](LICENSE).
