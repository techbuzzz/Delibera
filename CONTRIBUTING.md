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

- **Target framework:** .NET 8.0, C# 12 (`LangVersion 12.0`).
- **Modern C#:** file-scoped namespaces, `record` types, init-only properties, and global usings.
- **Nullable reference types** are enabled — keep the build warning-free.
- **XML documentation** is required on public APIs (`GenerateDocumentationFile` is on).
- **Naming:** `PascalCase` for types/methods, `camelCase` for locals, `_camelCase` for private fields.
- **Formatting:** four-space indentation; keep lines reasonably short and readable.
- The solution **must build with 0 errors and 0 warnings** before a PR is merged.

```bash
# Verify a clean build
dotnet clean && dotnet restore && dotnet build --configuration Release
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
