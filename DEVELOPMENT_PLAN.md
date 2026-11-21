# Nexa Development Plan

## Project Overview

Nexa is a Windows AI assistant that provides natural language control of the operating system. Users can press Alt+Space and type commands to execute actions like opening applications, controlling volume, searching files, and more.

## Architecture

### Solution Structure

```
Nexa/
├── Nexa.App                    # WPF application (UI layer)
│   ├── UI                      # XAML resources, styles, controls
│   ├── ViewModels              # MVVM view models
│   ├── Windows                 # Main windows (command palette, settings)
│   └── App.xaml                # Application entry point
│
├── Nexa.Core                   # Business logic layer
│   ├── AI                      # AI provider abstractions and implementations
│   ├── Actions                 # Action system (IAction implementations)
│   ├── Intent                  # Intent parsing and routing
│   ├── Models                  # Data models and DTOs
│   └── Interfaces              # Core interfaces
│
├── Nexa.Windows                # Windows OS integration layer
│   ├── AppLauncher             # Application discovery and launching
│   ├── FileSearch              # File system search
│   ├── Audio                   # Volume control and audio APIs
│   ├── Screenshot              # Screen capture functionality
│   ├── Shell                   # Windows shell integration
│   └── SystemInfo              # System information queries
│
├── Nexa.Infrastructure         # Cross-cutting concerns
│   ├── Settings                # Configuration management
│   ├── Logging                 # Structured logging
│   └── Security                # Credential storage (Windows DPAPI)
│
└── Nexa.Tests                  # Unit and integration tests
```

### Key Design Decisions

1. **MVVM Pattern**: Separates UI from business logic, enabling testability
2. **Dependency Injection**: All services injected via constructor injection
3. **Action System**: Each Windows capability is an independent `IAction` implementation
4. **Intent Routing**: Two-tier system (local fast parsing for simple commands, LLM for complex queries)
5. **Security**: API keys stored in Windows Credential Manager, never in plaintext
6. **Async/Await**: All I/O operations are asynchronous
7. **Cancellation Tokens**: Long-running operations support cancellation

## Dependencies

### NuGet Packages

**Nexa.App:**
- Microsoft.Extensions.DependencyInjection (DI container)
- Microsoft.Extensions.Hosting (generic host)
- CommunityToolkit.Mvvm (MVVM helpers)

**Nexa.Core:**
- Microsoft.Extensions.Logging.Abstractions
- System.Text.Json (JSON serialization)

**Nexa.Windows:**
- NAudio (audio control - robust Windows audio API)
- Microsoft.Windows.CsWin32 (P/Invoke for Windows APIs)

**Nexa.Infrastructure:**
- Microsoft.Extensions.Configuration.Json
- Serilog.Extensions.Logging (structured logging)

**Nexa.Tests:**
- xunit
- Moq
- FluentAssertions

### Framework Version
- .NET 8.0 (LTS)
- Windows 10.0.17763.0 minimum (Windows 10 October 2018 Update)

## Milestones

### Milestone 1: Core Infrastructure and Basic Actions

**Goal:** Create a working WPF application with system tray, global hotkey, and basic actions.

**Components:**
1. WPF application structure
2. System tray icon with context menu
3. Global hotkey registration (Alt+Space)
4. Command palette window (modern dark theme)
5. Action system infrastructure (IAction, ActionRouter)
6. Actions:
   - OpenApplicationAction
   - OpenFolderAction
   - SetVolumeAction
   - MuteVolumeAction
7. Basic settings (JSON-based configuration)
8. First-run wizard

**Success Criteria:**
- Application starts and shows in system tray
- Alt+Space opens command palette
- "Open Notepad" opens Notepad
- "Open Downloads" opens Downloads folder
- "Set volume to 30%" changes Windows volume
- "Mute" mutes the system
- Settings persist between runs

### Milestone 2: AI Integration

**Goal:** Integrate AI providers with tool calling support.

**Components:**
1. IAIProvider abstraction
2. OpenAICompatibleProvider (supports custom base URLs)
3. AnthropicProvider
4. Tool calling infrastructure (LLM → ActionRouter)
5. AI provider settings UI
6. Secure API key storage (Windows Credential Manager)
7. ActionRouter integration with AI responses

**Success Criteria:**
- Configure OpenAI-compatible endpoint in settings
- API key stored securely
- "Ask AI: What is MVVM?" returns LLM response
- Complex commands route through LLM for intent detection

### Milestone 3: Extended Features

**Goal:** Add file search, screenshots, voice, and system info.

**Components:**
1. FileSearchAction (search common user directories)
2. ScreenshotAction (capture screen/window)
3. ISpeechToTextService and Windows Speech implementation
4. ITextToSpeechService and Windows TTS implementation
5. SystemInfoAction (CPU, RAM, Windows version, disk space)
6. Voice input button in command palette
7. Voice response settings

**Success Criteria:**
- "Find newest PDF" shows recent PDFs
- "Take screenshot" captures and saves screenshot
- Microphone button captures voice input
- "What CPU do I have?" returns system info

### Milestone 4: Polish and Advanced Features

**Goal:** Add screen understanding, better matching, and command history.

**Components:**
1. IScreenContextService for screen analysis
2. Improved fuzzy matching for applications
3. Command history storage and UI
4. Performance optimizations (lazy loading, caching)
5. Error handling polish
6. Settings window completion
7. Full README documentation

**Success Criteria:**
- "What's on my screen?" analyzes screenshot
- Application search handles typos/abbreviations
- Command history shows recent commands
- README provides complete setup instructions

## Security Decisions

### API Key Storage
- Use Windows Credential Manager via DPAPI
- Never store keys in appsettings.json or user.config
- Credential target format: `Nexa/{ProviderName}`

### LLM Tool Calling
- AI never receives shell execution capabilities
- All actions validated by ActionRouter before execution
- Parameter validation on every action
- Risk levels: Safe, ConfirmationRequired, Blocked

### Privacy
- Command history stored locally only
- No telemetry without user consent
- Screenshot data only sent to AI with user consent
- File names in search results may be sent to AI (user configurable)

### Sensitive Actions
- Actions with `ConfirmationRequired` risk level show confirmation dialog
- No file deletion, registry modification, or shell execution in MVP
- Future dangerous actions will require explicit user approval

## Development Workflow

1. Create solution structure
2. Implement core interfaces and models
3. Build infrastructure layer (settings, logging, security)
4. Create Windows integration layer (launcher, audio, etc.)
5. Implement action system
6. Build WPF UI
7. Add AI providers
8. Test each milestone completely
9. Fix all compiler errors and test failures
10. Document in README.md

## Build Commands

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Run application
dotnet run --project Nexa.App
```

## Testing Strategy

- Unit tests for core logic (ActionRouter, intent parsing, parameter validation)
- Mock OS dependencies in tests
- Integration tests for AI providers (manual testing with real endpoints)
- No destructive OS operations in automated tests

## Future Roadmap

After MVP completion:
- Wake word detection
- Browser automation
- Advanced file operations
- Multi-monitor support
- Plugin system for custom actions
- Cloud sync for settings
- Mobile companion app
