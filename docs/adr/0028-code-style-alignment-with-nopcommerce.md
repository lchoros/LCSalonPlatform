# 0028 — Code style: what we take from the nopCommerce standards and where we stay as we are

Status: Decided — owner may veto (Tier 2: changes `.editorconfig`, which the build enforces)
Date: 2026-11-13

## Context
The owner follows the nopCommerce coding standards and wants the code to read that way. The architecture already
mirrors nopCommerce (layers, plugins, settings, localization). In style the repository differed in three places:

1. a blank line between groups of `using` directives, where nopCommerce has none;
2. braces on every block, where nopCommerce omits them on single statements;
3. constants in PascalCase, where nopCommerce writes `ALL_CAPS_WITH_UNDERSCORES`.

The alignment was held back until no feature PR was open, because it touches almost every file.

## Decision
- **`using` directives: aligned.** `dotnet_separate_import_directive_groups = false`, and the blank lines between
  groups are removed in every C# file except generated migrations. `System.*` first and alphabetical order stay.
  `dotnet format` does not remove existing blank lines, so a script did
  (`^using …;` followed by blank lines and another `using`, at column 0 only). The diff is one changed line in
  `.editorconfig` and 731 removed blank lines; nothing else.
- **Braces: we keep them on every block.** There is no code fix that removes braces, so the change would be made by
  hand in every `if`, `foreach` and `using` of the code base, and a slip there changes behaviour, not layout. Braces
  on every block are also the safer habit in code that handles money and tenant data. This is a deliberate deviation.
- **Constants: we keep PascalCase for now.** There are 221 constants, many of them public and used from Razor views.
  The Roslyn naming fix cannot be applied across a solution (`NamingStyleCodeFixProvider doesn't support Fix All in
  Solution`, tried on 2026-11-13), and it does not see `.cshtml` files. A rename by search and replace is the kind of
  change that passes the build and breaks a resource key or a route. PascalCase is also what the .NET guidelines and
  the analyzers in this repository expect.
- **Everything else in the standards already holds** and is enforced or habitual: naming of types, interfaces,
  private fields and generics, `var`, keywords over framework type names, modifier order, initialisers, pattern
  matching, Allman braces, `else` and `catch` on their own line, expression bodies for one-line members.

## Consequences
- New code is written with no blank lines between `using` groups; the formatter no longer adds them.
- If the owner wants `ALL_CAPS` constants after all, it is its own PR: constants renamed project by project with the
  IDE's rename refactoring (which follows references into Razor), a naming rule set to `error` so the build keeps it,
  and the full test suite between projects. About a day of careful work.
- If the owner wants braces omitted, the same applies, with a custom analyzer to keep it; I advise against it.

## Alternatives considered
- **Do all three now.** Two of the three cannot be done by a tool. Hand edits across 600 files for a cosmetic gain,
  with a real chance of a behaviour change, is a poor trade before the pilot.
- **Leave everything.** The `using` layout is the cheapest and most visible of the three and costs nothing to keep.

## Rollback
Revert the PR: the setting flips back and `dotnet format` restores the blank lines.
