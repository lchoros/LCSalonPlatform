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
1. Write a system prompt (role, language/framework versions, "output only the file, no prose") and a user prompt
   (task, acceptance criteria, the source files, one style example). Keep both as text files so the round can be replayed.
2. `pwsh scripts/ollama-task.ps1 -SystemFile sys.txt -PromptFile prompt.txt -OutFile <target file>`.
3. Build with `-warnaserror`, run the relevant tests, run `dotnet format`.
4. Feed the compiler output plus numbered review notes back as a second round (same system prompt, the current file
   appended). One fix round is usually enough; after two, finish by hand.
5. The Orchestrator reviews the result like any other diff and commits it with a note that the draft came from the
   local model.

## Lessons from the first run (2026-11-11, `tests/Salon.Tests.Unit/Properties`)
- Give the exact FsCheck 3 API names; the model defaults to FsCheck 2 (`Arb.From`, `Gen.String`).
- Say "no method parameters on `[Property]` methods" or it shadows them with lambda parameters.
- Spell out invisible characters (no-break space U+00A0) and rounding modes; it assumed plain spaces and to-even
  rounding, and one of its own properties then caught that `N2` rounds away from zero.
- It once emitted `file-scoped namespace …;` as literal text when told to use a file-scoped namespace; show an
  example instead of describing the style.
- The analyzers (CA1310, CA1304, CA1311) need a reminder: pass `StringComparison.Ordinal`, use `ToUpperInvariant`.
