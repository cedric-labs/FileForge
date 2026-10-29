# Instructions for AI Agents & Software Engineering Standards (AGENTS.md)

This document sets the software quality standards, the mandatory tooling protocols and the non-negotiable rules for the **Anima** project. Every agent working on this codebase must follow them strictly.

---

## 1. Mandatory Initialization Protocols (From First Contact)

### 1.1. Git Repository
- **Immediate check**: As soon as you take over a project, check whether the folder is a Git repository (`git rev-parse --is-inside-work-tree`).
- **Initialization**: If there is no repository:
  1. Run `git init`.
  2. Add the official, complete .NET `.gitignore` (`bin/`, `obj/`, `.vs/`, `.sonarqube/`, `TestResults/`, logs, caches).
  3. Make a clean initial commit.
- **Commit cycle**: Each feature batch, refactoring or fix validated by the tests gets its own atomic Git commit following *Conventional Commits* (`feat:`, `fix:`, `refactor:`, `test:`, `style:`, `chore:`).

### 1.2. SonarQube Integration
- **SonarQube is not optional**: It is the final judge of code quality.
- **Provisioning**: When the quality harness is installed, make sure the project is registered on the local instance (`http://localhost:9000`) with the project key `Anima`.
- **Continuous validation**: The `./verify.ps1` script runs the SonarScanner analysis. No task is complete until it reports **QUALITY GATE STATUS: PASSED**.

---

## 2. Golden Rules of "Human Code" & Code Cleanliness

### Rule 1: No warning suppression
- **Absolutely forbidden**: suppression directives and attributes:
  - `#pragma warning disable`
  - `// NOSONAR`
  - `[SuppressMessage]`
  - The null-forgiving operator `!` (in application and domain code).
- **Principle**: Always fix the root cause with strict typing (`Nullable<T>`, explicit guard clauses `ArgumentNullException.ThrowIfNull`, pattern matching checks `is not null`).

### Rule 2: No silent `catch` block
- **Forbidden**: silent exception blocks (`catch { }` or `catch (Exception ex) { }` with no action).
- Catch **only** explicitly expected typed exceptions (e.g. `IOException`, `UnauthorizedAccessException`).
- When catching, always:
  1. Log the problem with structured logging (`ILogger` or `RaiseLog`), **or**
  2. State in the block the compelling reason why the exception is tolerated.

### Rule 3: No catch-all classes
- **Forbidden**: creating or keeping classes, folders or namespaces with generic suffixes or prefixes:
  - `*Helper*`
  - `*Utils*`
  - `*Manager*`
  - `*Common*`
- Each component must be named after its single business responsibility:
  - `*Calculator` (metrics, predictive algorithms)
  - `*Validator` (compliance rules, disk validation)
  - `*Parser` / `*Formatter` / `*Serializer` (format conversion, XML, text)
  - `*Provider` / `*Resolver` (identifier resolution, system discovery)
  - `*Advisor` (hardware recommendations, heuristics)
  - `*Dashboard` / `*Reporter` (UI rendering and console display)

### Rule 4: No cosmetic splitting
- When a method has a high cyclomatic or cognitive complexity, never chop it into meaningless technical fragments (`Part1`, `Part2`, `SubMethodA`).
- Extract coherent domain abstractions instead: a dedicated component, a Value Object, a polymorphic strategy or a clear functional pipeline.

### Rule 5: Human readability & "Why" comments
- **Guard clauses**: Check preconditions at the start of methods (`if (condition) return ...;`) to keep indentation to at most 2 levels.
- **Immutability by default**: Prefer immutable structures (`readonly`, `record`, `init` accessors, read-only collections `IReadOnlyList<T>`).
- **Useful comments**:
  - **NEVER** paraphrase the code (the code must be expressive and self-documenting).
  - Comments explain only the **WHY**: non-obvious architecture decisions, workarounds for OS/terminal bugs, performance requirements, critical business invariants.

### Rule 6: No AI lexical markers (plain, factual naming)
- **Forbidden**: flattering, vague qualifiers or AI verbal tics in the names of methods, classes, interfaces, variables, commits and comments:
  - `*Robust*`
  - `*Resilient*`
  - `*Seamless*`
  - `*Smart*`
  - `*Advanced*`
  - `*Comprehensive*`
  - `*Ultimate*`
- **Principle**: Name by the **factual technical intent**, not by a subjective self-assessment of the code quality:
  - ❌ `DeleteDirectoryRobust` ➔ ✔️ `ForceDeleteDirectory` or `DeleteDirectoryWithRetries`
  - ❌ `RobustCopier` ➔ ✔️ `FileCopier`
  - ❌ `SeamlessRunner` ➔ ✔️ `SequentialTaskRunner`

### Rule 7: Test naming standard (`MethodName_Condition_ExpectedResult`)
- **Mandatory naming**: Every unit or integration test follows the pattern `TargetMethod_ScenarioOrCondition_ExpectedResult` (e.g. `Resolve_WhenUncPathHasDoubleBackslash_ReturnsNormalizedDiskId`, `CopyAsync_WhenTargetIsReadOnly_ThrowsUnauthorizedAccessException`).
- **Principle**: A failing test in the CI / SonarQube report must read as a broken business specification, without having to open the test body.

### Rule 8: Strong typing & no Primitive Obsession
- **Forbidden**: passing primitive types (`string`, `int`, `long`) around for business concepts that carry validation rules, invariants or specific formats (e.g. volume identifiers, normalized paths, hashes).
- **Principle**: Wrap these concepts in immutable Value Objects (`readonly record struct` or sealed `record class`) validated when they are created.

### Rule 9: Infrastructure decoupling & dependency inversion
- The domain and application core must **never** depend directly on concrete infrastructure (direct calls to `Console.*`, raw disk access without abstraction, native OS APIs).
- Every access to external resources or devices goes through clear injected abstractions (`ILogger`, `IFileSystem`, `IVolumeResolver`), so that unit tests need no complex workarounds.

### Rule 10: Clean asynchrony & cooperative cancellation
- **Token propagation**: Every asynchronous or I/O method takes a `CancellationToken cancellationToken = default` parameter and passes it to every method it calls.
- **Honor cancellation**: Never ignore a cancellation token or swallow an `OperationCanceledException` without rethrowing it, except for an explicitly documented graceful shutdown.

### Rule 11: C# 12 primary constructors for injection
- **Idiomatic syntax**: Always use primary constructors to declare and inject the dependencies of classes and services (e.g. `public sealed class VolumeSnapshotProvider(ILogger logger, IFileSystem fileSystem) : IVolumeSnapshotProvider`).
- **No boilerplate**: Do not declare redundant `private readonly` fields when the primary constructor is enough. Check preconditions at initialization when needed.

### Rule 12: XML documentation of public contracts
- **Documented contracts**: Every public interface, interface method, public enum and contract property has complete XML documentation (`///`) (`<summary>`, `<param>`, `<returns>`, `<exception>`).
- **No noise on internal code**: Do not clutter private, local or obvious methods with mechanical XML comments. Keep XML documentation for the entry points and abstractions of the system.

### Rule 13: Mandatory E2E & UI tests with Playwright (Web projects)
- **Scope**: As soon as a project is a Web project (Blazor, ASP.NET Core MVC / Razor Pages, or a Web UI served by .NET), installing **Playwright** and setting up a dedicated E2E test suite are mandatory.
- **UI coverage**:
  - **Display & structural rendering**: Check that containers, tables, forms, navigation bars and visual elements are present, visible and laid out correctly.
  - **Full interactivity**: Trigger and validate 100% of buttons, navigation links, input fields, checkboxes, drop-down menus and modal windows.
  - **Data accuracy**: Assert explicitly on the exact values displayed on screen (computed texts, amounts, counters, dynamic labels, confirmation or error messages).
- **Mandatory Page Object Model (POM) architecture**:
  - Raw CSS or XPath selectors are strictly forbidden in test methods.
  - Wrap each page or reusable component in a dedicated Page Object class (e.g. `LoginPage`, `DashboardPage`, `NavbarComponent`) exposing business action methods and typed accessors.
- **Strict test naming**: The Rule 7 pattern is mandatory (e.g. `DashboardPage_WhenLoaded_DisplaysAccurateCounterValues`, `ExportButton_WhenClicked_TriggersDownload`).
- **Automated run**: Playwright tests must run on their own in headless mode (no visible UI) during the `./verify.ps1` pipeline.

### Rule 14: Systematic reuse & capitalization in the shared frameworks
- **Inspect first**: Before implementing a technical component, a utility, an I/O operation, hashing, an algorithm or terminal rendering that is not business-specific, check the central catalog of shared frameworks (`D:\Dev\Framework\FRAMEWORKS_INDEX.md`).
- **No duplication**: Never rewrite locally a building block that already exists in a shared framework. Reference the relevant project (`<ProjectReference>`).
- **Backward-compatible evolution & overloads**: When a framework feature must be extended or adapted:
  1. Change the component directly in the host framework through method overloads or optional parameters.
  2. Strictly preserve the existing contracts so that no other consumer project gets a breaking change.
- **New framework & name approval**: When a generic feature fits no existing framework and needs a new one, the agent must not create the project on its own. It must propose 2 or 3 plain, factual names (no AI buzzwords) to the user and wait for explicit approval before initializing the framework.
- **Documentation update & mandatory validation**:
  1. Update the `FRAMEWORK.md` file of the framework as soon as a capability is added or changed.
  2. Run `./verify.ps1` at the framework root and make sure the SonarQube Quality Gate is **PASSED** before using the component in the client application.

---

## 3. Automated Tooling Harness

The project relies on a set of strict automated checks declared at solution level:

1. **Global Roslyn analyzers (`Directory.Build.props`)**:
   - `Roslynator.Analyzers` (modern C# best practices and refactorings).
   - `Meziantou.Analyzer` (API pitfalls, security and .NET performance).
   - `AsyncFixer` (asynchronous anti-patterns and deadlocks).
   - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (any warning breaks the build).

2. **Style & formatting (`.editorconfig`)**:
   - `UTF-8` encoding, `CRLF` line endings, 4-space indentation.
   - File-scoped namespaces.
   - Automatic sorting of `using` directives (System first).
   - `dotnet format --verify-no-changes` always run.

3. **Single validation pipeline (`verify.ps1`)**:
   The root `./verify.ps1` script runs the full chain and is the only acceptance condition for a change:
   - [1/6] Diagnostics & tool restore.
   - [2/6] Formatting compliance (`dotnet format`).
   - [3/6] SonarScanner initialization.
   - [4/6] Non-incremental Release build with warnings as errors (`-warnaserror`).
   - [5/6] Run 100% of unit, architecture and Playwright E2E tests (headless) with OpenCover coverage.
   - [6/6] SonarQube upload and Quality Gate wait (`PASSED`).
