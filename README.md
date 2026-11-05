# AI Code Review Agent (ASP.NET Core & C#)

A beginner-friendly, clean, and extensible **AI Code Review Agent** built with **C# and ASP.NET Core Web API (.NET 8)** and an OpenAI-compatible LLM endpoint (such as **NVIDIA NIM**).

This project allows developers to paste a Git diff or upload a `.patch` file. The AI agent inspects the changes, iteratively calls dedicated C# analysis tools to gather facts (security patterns, test coverage, code quality heuristics), and returns structured JSON review findings that are rendered in an interactive frontend.

---

## 📑 Table of Contents
1. [Key Features](#key-features)
2. [Architecture Overview](#architecture-overview)
3. [AI Agent Workflow & Tool Calling](#ai-agent-workflow--tool-calling)
4. [Project Structure](#project-structure)
5. [Prerequisites](#prerequisites)
6. [Getting Started & Configuration](#getting-started--configuration)
   - [Option A: .NET User Secrets (Recommended for Local Dev)](#option-a-net-user-secrets-recommended-for-local-dev)
   - [Option B: Environment Variable](#option-b-environment-variable)
7. [Running the Application](#running-the-application)
   - [Visual Studio](#in-visual-studio)
   - [VS Code / Terminal](#in-vs-code-or-command-line)
8. [Running Unit Tests](#running-unit-tests)
9. [API Endpoints Reference](#api-endpoints-reference)
10. [Sample Diff for Testing](#sample-diff-for-testing)

---

## 🌟 Key Features

- **Standard Unified Diff Parser**: Parses raw Git diffs into structured C# models (`DiffSummary`, `ChangedFile`, `DiffHunk`, `DiffLine`) tracking added/removed line numbers.
- **Agentic Workflow with Tool Calling**: The AI model decides when and which tools to invoke (`scan_security_issues`, `check_test_coverage`, `analyze_code_quality`, `get_file_info`, `get_file_diff_snippet`) before producing its final conclusions.
- **Safety First**:
  - Uploaded code is **never executed**.
  - Iteration safety ceiling (`MaxToolCallIterations`) prevents infinite agent loops and uncontrolled API spend.
  - Distinguishes **Confirmed** defects from **Potential** concerns.
  - Strictly grounded in the diff to avoid inventing findings.
- **Modern .NET 8 Best Practices**:
  - Typed `HttpClient` via `IHttpClientFactory` avoiding socket exhaustion.
  - Options pattern (`IOptions<NvidiaAiSettings>`) for strongly-typed configuration.
  - Async/await and `CancellationToken` support throughout.
  - Idiomatic dependency injection (DI).
- **Lightweight Zero-Dependency Frontend**: Responsive, clean HTML5/CSS3/Vanilla JS UI with live severity filtering, one-click sample loader, and file drag-and-drop.

---

## 🏛️ Architecture Overview

The application follows a clean separation of concerns without over-engineering:

```
[ Browser Frontend (wwwroot) ]
         │
         │ HTTP POST /api/review
         ▼
[ ReviewController ] ──────────────► Basic request validation & file reading
         │
         ▼
  [ AgentService ] ────────────────► Orchestrates the review loop & state
     │          │
     │          ├──► [ DiffParserTool ]  (Parses unified diff into data structures)
     │          │
     │          ├──► [ CodeAnalysisTool ] (Executes requested tool calls:
     │          │                         security, test-check, quality scans)
     │          │
     ▼          ▼
   [ LlmService ] ─────────────────► Communicates with NVIDIA NIM / OpenAI API
         │
         ▼
[ NVIDIA NIM API ]
```

---

## 🤖 AI Agent Workflow & Tool Calling

Unlike a simple "prompt-and-pray" LLM call, this agent runs an iterative reasoning loop:

```
                ┌──────────────────────────────────────┐
                │ 1. Receive diff & parse via C# Tool  │
                └──────────────────┬───────────────────┘
                                   │
                ┌──────────────────▼───────────────────┐
                │ 2. Prepare conversation with diff    │
                │    overview & available tool schemas │
                └──────────────────┬───────────────────┘
                                   │
       ┌───────────────────────────▼───────────────────────────┐
 ┌────►│ 3. Send request to NVIDIA NIM chat/completions API     │
 │     └───────────────────────────┬───────────────────────────┘
 │                                 │
 │                     Does model request a tool call?
 │                                ╱ ╲
 │                        Yes    ╱   ╲   No
 │                       ┌───────     ────────┐
 │                       ▼                    ▼
 │             ┌──────────────────┐    ┌─────────────────────┐
 │             │ 4. Dispatch to   │    │ 6. Parse structured │
 │             │    C# method:    │    │    JSON findings    │
 │             │ - security scan  │    └──────────┬──────────┘
 │             │ - test coverage  │               │
 │             │ - code quality   │               ▼
 │             └─────────┬────────┘    ┌─────────────────────┐
 │                       │             │ 7. Return response  │
 │             ┌─────────▼────────┐    │    to user frontend │
 │             │ 5. Append result │    └─────────────────────┘
 └─────────────┤    as tool role  │
               └──────────────────┘
```

### Available Tools:
1. `scan_security_issues`: Detects hard-coded passwords, secrets, connection strings, SQL injection via concatenation, and weak algorithms.
2. `check_test_coverage`: Inspects if test file paths accompany production changes.
3. `analyze_code_quality`: Flags magic numbers, long lines (>120 chars), lingering `Console.WriteLine` statements, and empty `catch` blocks.
4. `get_file_info`: Extracts file extension, programming language, hunk count, and lines changed.
5. `get_file_diff_snippet`: Retrieves formatted diff context for a specific file to keep token usage small.

---

## 📁 Project Structure

```
CodeAgent/
├── AICodeReviewAgent.sln               # Solution file
├── sample.patch                        # Real sample Git diff file for testing
├── README.md                           # Documentation and interview guide
├── AICodeReviewAgent/                  # ASP.NET Core Web API project
│   ├── Configuration/
│   │   └── NvidiaAiSettings.cs         # Strongly-typed configuration model
│   ├── Controllers/
│   │   └── ReviewController.cs         # API routes (/api/review, /upload, /sample)
│   ├── Models/
│   │   ├── DiffModels.cs               # DiffSummary, ChangedFile, DiffHunk, DiffLine
│   │   ├── OpenAiModels.cs             # ChatCompletionRequest, ToolDefinition, etc.
│   │   └── ReviewModels.cs             # ReviewRequest, ReviewResponse, ReviewFinding
│   ├── Services/
│   │   ├── AgentService.cs             # Agentic loop orchestration & tool dispatch
│   │   └── LlmService.cs               # Typed HttpClient for NVIDIA NIM
│   ├── Tools/
│   │   ├── CodeAnalysisTool.cs         # Static analysis methods (security, tests, quality)
│   │   └── DiffParserTool.cs           # Unified Git diff parsing logic
│   ├── wwwroot/                        # Frontend assets
│   │   ├── index.html                  # Responsive UI layout
│   │   ├── style.css                   # Custom CSS styling
│   │   └── app.js                      # Vanilla JavaScript controller
│   ├── appsettings.json                # Safe default configuration (no secrets)
│   ├── appsettings.Development.json    # Development logging settings
│   └── Program.cs                      # Dependency injection & middleware pipeline
└── AICodeReviewAgent.Tests/            # xUnit Test project (36 tests)
    ├── DiffParserToolTests.cs          # Tests for parsing, hunks, lines, change types
    └── CodeAnalysisToolTests.cs        # Tests for security patterns, test-check, quality
```

---

## 📋 Prerequisites

- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** or later.
- An **NVIDIA NIM API key** (free access available at [build.nvidia.com](https://build.nvidia.com)).
  - Any model supporting OpenAI-compatible tool/function calling works (default: `meta/llama-3.1-70b-instruct`).

---

## ⚙️ Getting Started & Configuration

### Option A: .NET User Secrets (Recommended for Local Dev)
User secrets keep your API keys securely on your local machine outside the source code repository:

```bash
cd AICodeReviewAgent
dotnet user-secrets init
dotnet user-secrets set "NvidiaAI:ApiKey" "nvapi-your-key-here"
```

### Option B: Environment Variable (CI / Docker / Shell)
Set the environment variable using double underscores for nested keys:

**Windows PowerShell:**
```powershell
$env:NvidiaAI__ApiKey = "nvapi-your-key-here"
```

**macOS / Linux:**
```bash
export NvidiaAI__ApiKey="nvapi-your-key-here"
```

### Configuration in `appsettings.json`:
```json
{
  "NvidiaAI": {
    "ApiKey": "",
    "BaseUrl": "https://integrate.api.nvidia.com/v1",
    "ModelName": "meta/llama-3.1-70b-instruct",
    "MaxToolCallIterations": 5
  }
}
```

---

## 🚀 Running the Application

### In VS Code or Command Line:
```bash
# Navigate to the project folder
cd AICodeReviewAgent

# Run the app
dotnet run
```
Once started, open your browser and navigate to:
👉 **`http://localhost:5000`** (or the URL printed in your console).

### In Visual Studio:
1. Open `AICodeReviewAgent.sln`.
2. Right-click `AICodeReviewAgent` and click **Set as Startup Project**.
3. Press **F5** or click the green **Run** button.
4. The browser opens automatically showing the review interface.

---

## 🧪 Running Unit Tests

The test project contains **36 unit tests** covering diff parsing, hunk classification, added-line extraction, security pattern detection, missing test detection, and code quality heuristics.

```bash
dotnet test
```

Expected output:
```
Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 400 ms
```

---

## 📡 API Endpoints Reference

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/review` | Accepts JSON `{ "diffContent": "...", "repositoryContext": "..." }` and returns structured review findings. |
| `POST` | `/api/review/upload` | Accepts multipart form upload of a `.patch`, `.diff`, or `.txt` file (max 1 MB). |
| `GET` | `/api/review/sample` | Returns a sample Git diff for quick UI testing. |

### Sample JSON Review Finding:
```json
{
  "summary": "Review complete. 2 file(s) analysed. Found 5 issue(s): 2 High, 2 Medium, 1 Low.",
  "toolCallsExecuted": 4,
  "reviewedAt": "2026-11-05T02:00:00.000Z",
  "findings": [
    {
      "severity": "High",
      "category": "Security",
      "fileName": "src/UserService.cs",
      "lineNumber": 11,
      "explanation": "Hard-coded connection string containing credentials in source code.",
      "suggestedFix": "Store connection strings in appsettings.json or Azure Key Vault and inject via IConfiguration.",
      "confidence": "Confirmed"
    },
    {
      "severity": "High",
      "category": "Security",
      "fileName": "src/UserService.cs",
      "lineNumber": 20,
      "explanation": "Potential SQL injection vulnerability due to string concatenation.",
      "suggestedFix": "Use parameterized queries with SqlParameter or an ORM like EF Core / Dapper.",
      "confidence": "Confirmed"
    },
    {
      "severity": "Medium",
      "category": "MissingTests",
      "fileName": "src/OrderProcessor.cs",
      "lineNumber": 1,
      "explanation": "New OrderProcessor class added without accompanying unit tests.",
      "suggestedFix": "Create tests covering discount calculation tiers (types 1, 2, 3) and default case.",
      "confidence": "Potential"
    }
  ]
}
```

---

## 🔍 Sample Diff for Testing

The file `sample.patch` in the repository root contains an authentic patch altering `UserService.cs` and adding `OrderProcessor.cs`. It tests:
1. **Security**: Hard-coded credentials and SQL concatenation.
2. **Missing Tests**: Production changes without corresponding test files.
3. **Code Quality**: Swallowed exceptions in `catch (Exception) {}` and `Console.WriteLine` left in production.
4. **Style / Maintainability**: Magic numbers and `TODO`/`FIXME` markers.

we can load this sample instantly by clicking **"📄 Load Sample Diff"** in the web UI.

---
