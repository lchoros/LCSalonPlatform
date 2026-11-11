# Local models (Ollama) — what the Orchestrator delegates to them

The owner's machine runs Ollama (`http://localhost:11434`) with `qwen2.5-coder:32b` (default for code, ~30 tokens/s
on the GPU, ~45 s cold start), `deepseek-r1:32b` (reasoning, slower) and `qwen2.5-coder:7b` (fast, weaker). Using them
saves cloud usage for the work that needs judgement.

## Division of labour
| Goes to the local model | Stays with the Orchestrator (Claude) |
| --- | --- |
| first drafts of unit and property tests from a brief + the source files | architecture, ADRs, anything touching tenancy, payments, auth |
| en-GB → pl-PL translation drafts for new resource keys | review of every diff before it is pushed or merged |
| realistic demo data (service names, descriptions, prices) | migrations and schema changes |
| mechanical rewrites (rename, reorder, XML doc summaries) | fixing the local model's output when it drifts |

Rule of thumb: delegate when the result can be checked mechanically (it compiles, tests pass, parity tests pass) and
the brief fits in one message with the relevant source files pasted in.

## How a delegation runs
Default (saves the most cloud usage): the autonomous loop.
1. Write a system prompt (role, language/framework versions, exact library API names, "output only files, each
   starting with `// FILE: <path>`") and a task prompt (acceptance criteria, the source it depends on pasted in, one
   style example). Keep both as text files so the round can be replayed.
2. `pwsh scripts/ollama-loop.ps1 -Root <worktree> -SystemFile sys.txt -PromptFile task.txt -BuildProjects <proj>
   -TestProject <tests> -TestFilter "FullyQualifiedName~X"` — it writes the files, formats them, builds with
   `-warnaserror`, runs the tests and feeds compiler/test output back to the model, up to three rounds, then prints a
   JSON summary (`rounds`, `build`, `tests`, `lastErrors`, `files`).
3. The Orchestrator reads only the summary and the diff, fixes what is left by hand (usually API-level slips), reviews
   it like any other change and commits it with a note that the draft came from the local model.

Single-shot alternative for text or one file: `pwsh scripts/ollama-task.ps1 -SystemFile sys.txt -PromptFile prompt.txt
-OutFile <target>` and the same review.

Since 2026-11-11 evening the local model is the default builder for ordinary slices and tests; Claude builders are
kept for tenancy, authentication, migrations and anything that needs design judgement.

## Lessons from the first run (2026-11-11, `tests/Salon.Tests.Unit/Properties`)
- Give the exact FsCheck 3 API names; the model defaults to FsCheck 2 (`Arb.From`, `Gen.String`).
- Say "no method parameters on `[Property]` methods" or it shadows them with lambda parameters.
- Spell out invisible characters (no-break space U+00A0) and rounding modes; it assumed plain spaces and to-even
  rounding, and one of its own properties then caught that `N2` rounds away from zero.
- It once emitted `file-scoped namespace …;` as literal text when told to use a file-scoped namespace; show an
  example instead of describing the style.
- The analyzers (CA1310, CA1304, CA1311, CA1510) need a reminder: pass `StringComparison.Ordinal`, use
  `ToUpperInvariant`, use `ArgumentNullException.ThrowIfNull`.
- Third run (loop trial, FsCheck properties for `LockoutPolicy`): three rounds of compiler feedback did not cure
  FsCheck-2 habits (`Gen` passed straight to `Prop.ForAll`, `Gen.OneOf` without type arguments, `Sample()`). Ask the
  local model for plain xunit `[Theory]`/`[Fact]` tests; write property tests by hand or give it a complete
  compiling FsCheck example to copy from.
- Second run (message-template renderer): the texts and tests were usable after one round; the renderer logic was not
  (a regex group under `ExplicitCapture` captured nothing, output assembled in the wrong order). For logic with
  edge cases, have the model write the tests first and let the loop's failures expose a wrong implementation, or
  write the logic yourself and delegate only the tests.
