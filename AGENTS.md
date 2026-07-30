
# General

- Code must be thread safe and compatible with both linux and windows os
- Debugging logs/lines must be locked behind `DevMode.IsEnabled` or their devmode alternatives in utilities\logging\, specially verbose ones. Errors tipically should not be devmode specific.
- `VisualBrush` usage should be avoided and only used as a last resort, instead use tools like `TintedAssetConverter`.
- Prefer using clean, scalable implementations, rather than quick solutions; even if that means refactoring code.

## Codebase structure

- To add a color to styles, see `style.dark.json` and `style.light.json` -> `Style.cs` -> (usually) `WindowThemeManager.cs`.
- To save a setting in config, see `ModHearthConfig` -> `ConfigManager`.

## Git Workflow

- Use `git add` commands in the development branch instead of creating GitHub pull requests.
- Avoid making remote branch changes. Use origin/development instead.
- If `git add` causes issues, create a new local branch using `git checkout`.
