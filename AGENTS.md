
# General

- Code must be thread safe and compatible with both linux and windows os
- Debugging logs/lines must be locked behind DevMode.IsEnabled or their devmode alternatives in utilities\logging\, specially verbose ones

## Git Workflow

- Use `git add` commands in the development branch instead of creating GitHub pull requests.
- Avoid making remote branch changes. Use origin/development instead.
- If `git add` causes issues, create a new local branch using `git checkout`.
