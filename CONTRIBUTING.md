<div align="center">

<img src="delibera-horizontal-1920x480.png" alt="Delibera" width="480" />

# Contributing to Delibera

### ⚖️ Thoughtful AI Decisions

</div>

Thank you for your interest in contributing to **Delibera**! This document explains how to set
up your environment, our coding standards, and the pull-request workflow.

---

## 🧭 Code of Conduct

Be respectful, constructive, and inclusive. We deliberate — we don't dominate. Treat every
contributor and idea with the same fairness Delibera brings to AI debates.

---

## 🛠️ Development Setup

**Prerequisites**

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional) Docker, for running Qdrant or PostgreSQL/pgvector locally

```bash
# Fork & clone
git clone https://github.com/<your-username>/Delibera.git
cd Delibera

# Restore & build
dotnet restore
dotnet build --configuration Release

# Run the console demo
cd Delibera.ConsoleApp
dotnet run
```

---

## 📐 Coding Standards

- **Target framework:** .NET 10.0 (`net10.0`), C# with `LangVersion preview`.
- **Modern C#:** file-scoped namespaces, `record` types, init-only properties, and global usings.
- **Nullable reference types** are enabled — keep the build warning-free.
- **XML documentation** is required on public APIs (`GenerateDocumentationFile` is on).
- **Naming:** `PascalCase` for types/methods, `camelCase` for locals, `_camelCase` for private fields.
- **Formatting:** four-space indentation in tests, three-space in `Delibera.Core`; match the
  file you are editing.
- The solution **must build with 0 errors and 0 warnings** before a PR is merged, and CI
  builds with `-warnaserror` so a new warning fails the build.

```bash
# Verify a clean build
dotnet clean && dotnet restore && dotnet build --configuration Release
```

### 🧪 Testing rules

**A test of a callback, event or continuation must use a provider that suspends at least
once before completing.** `FakeLLMProvider` suspends by default for exactly this reason.

A fake that returns `Task.FromResult` completes synchronously, so the code under test runs
to the end inline. Anything that is only valid while nothing has suspended — a handler
installed on a field and uninstalled in a `finally`, for instance — looks correct and is
dead against a real provider, which awaits network I/O.

This is not hypothetical: the SSE streaming path had eleven passing tests and returned **no
rounds at all** in production, because the interceptor was uninstalled before the first real
await. If a test genuinely needs synchronous completion, ask for it explicitly
(`new FakeLLMProvider(suspends: false)`) and say why in the test.

```bash
dotnet test --configuration Release
```

---

## 🌿 Branch & Commit Conventions

- Create a feature branch: `git checkout -b feature/short-description`.
- Write clear, imperative commit messages (e.g., `Add consensus debate timeout option`).
- Reference related issues in the body (e.g., `Closes #42`).

---

## 🔀 Pull Request Process

1. Ensure your branch is up to date with `main`.
2. Confirm the solution builds clean (0 errors, 0 warnings) and examples run.
3. Update documentation (`README.md`, `QuickStart.md`) when behaviour changes.
4. Open a PR using the [pull-request template](.github/PULL_REQUEST_TEMPLATE.md) and fill in all sections.
5. A maintainer will review; address feedback by pushing additional commits.

---

## 🐛 Reporting Issues

Please use the issue templates under `.github/ISSUE_TEMPLATE/`:

- **Bug report** — for unexpected behaviour, with reproduction steps.
- **Feature request** — for new ideas and enhancements.

---

## 📄 License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE).

---

<div align="center">

**⚖️ Delibera — Thoughtful AI Decisions**

</div>
