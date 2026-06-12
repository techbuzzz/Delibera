# Commit Style Guide

Follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) for all commit messages.

## Format
```
<type>(<scope>): <short description>
```

## Types
- `feat:` — new feature
- `fix:` — bug fix
- `docs:` — documentation only
- `style:` — formatting, no logic change
- `refactor:` — code restructuring without behavior change
- `test:` — adding or updating tests
- `chore:` — maintenance, dependency updates, build changes
- `perf:` — performance improvement
- `ci:` — CI/CD changes

## Examples
```
feat(invoices): add AI text parsing endpoint
fix(auth): resolve refresh token expiry edge case
docs(readme): update deployment instructions
refactor(services): extract invoice number generation to domain
test(invoices): add unit tests for subscription limit check
chore(deps): update GemBox.Document to 2025.12
```

## Branch Naming
- `feature/{ticket}-{short-description}`
- `fix/{ticket}-{short-description}`
- `hotfix/{ticket}-{short-description}`
- `chore/{ticket}-{short-description}`

## PR Template
```
## Description
What does this PR do?

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update
- [ ] Refactoring

## Testing
- [ ] Tested locally
- [ ] Unit tests added/updated
- [ ] All tests pass

## Checklist
- [ ] Code follows style guidelines
- [ ] Self-review completed
- [ ] Documentation updated if needed
```
