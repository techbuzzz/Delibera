# W0 — Build & Toolchain

> **Goal:** the solution builds for anyone who clones it, and version drift cannot come back.
> **Baseline:** `dotnet build -c Release` → 0 errors, 15 warnings (2 × NU1902, 13 × compiler).

---

## W0-01 · Align NuGet versions across test projects · **P0** · ✅ done

**Problem.** `Delibera.Core` referenced `Microsoft.Extensions.AI 10.8.0` and
`Microsoft.Extensions.* 10.0.10`, while both test projects referenced `10.7.0` / `10.0.9`.
NuGet treats a downgrade as an error, so the whole solution failed to restore:

```
error NU1605: Detected package downgrade: Microsoft.Extensions.AI from 10.8.0 to 10.7.0
```

The last release notes claim "402 unit tests pass". On `feature/v10.3.1` as checked out,
`dotnet build` failed and **no test could run at all**.

**Fix.** Bump the eight references in `tests/Delibera.Core.Tests/Delibera.Core.Tests.csproj`
and four in `tests/Delibera.Server.Tests/Delibera.Server.Tests.csproj` to the versions
used in `src/`.

**Acceptance.**
- [x] `dotnet build Delibera.slnx -c Release` → 0 errors
- [x] `dotnet test Delibera.slnx -c Release` → green (403 passed at the time of the fix; 412 after this pass)

---

## W0-02 · Bump `Microsoft.SourceLink.Git` (NU1902) · **P1** · ✅ done

**Problem.**
```
warning NU1902: Package 'Microsoft.Build.Tasks.Git' 10.0.301 has a known moderate
severity vulnerability — GHSA-23fw-v26w-5fgq
```
`Delibera.Core.csproj:75` pins `Microsoft.SourceLink.Git 10.0.301`, which pulls the
affected `Microsoft.Build.Tasks.Git` transitively. For a library that embeds source into
its symbol package, shipping a flagged build component is a supply-chain smell.

**Fix.** Raise the `Microsoft.SourceLink.Git` reference to the first version that
transitively brings a non-vulnerable `Microsoft.Build.Tasks.Git`; if the reference itself
is the only pin, pin the patched task package explicitly.

**Acceptance.**
- [ ] `dotnet build` reports no NU1902
- [ ] `git ls-remote`-free check: no NU1902 in CI log

---

## W0-03 · Single version source (Central Package Management) · **P2** · 🔒 10.4.0

**Problem.** Five `.csproj` files each list their own `PackageReference` versions;
`Microsoft.Extensions.*` appears in four of them at three different version levels. W0-01
is the recurring cost of that arrangement. There is no `Directory.Packages.props` and no
`Directory.Build.props` in the repository.

**Fix.** Introduce `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`
and strip `Version=` attributes. It is deliberately a **10.4.0** task: converting five
projects produces a diff that obscures everything else in the same release.

**Acceptance.**
- [ ] One place defines each package version
- [ ] `dotnet build` produces no NU1605/NU1507
- [ ] CHANGELOG notes the layout change for contributors
