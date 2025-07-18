# Personal Coding Preferences

## Shell Preferences
- **ALWAYS use `pwsh` instead of `powershell`** - PowerShell Core is preferred
- Before running PowerShell scripts, set execution policy: `pwsh -Command "Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process"`

## Code Style Guidelines
- **Brace Style**: Allman style (opening brace on new line)
- **Indentation**: 4 spaces, no tabs
- **Naming**: 
  - PascalCase for public members
  - _camelCase for private fields (with underscore prefix)
  - Async methods end with `Async` suffix
- **Access Modifiers**: Always explicit
- **Modern C# Features**: Use `var`, pattern matching, null-conditional operators, expression-bodied members where appropriate
- **Comments**: Minimal - only for complex logic

## KISS Principle
- Always prefer the simplest solution that works
- Use built-in functionality (like `enum.ToString()`) over custom implementations  
- Inline simple lambdas instead of extracting to variables
- Trust the framework - don't defensively code for non-existent problems