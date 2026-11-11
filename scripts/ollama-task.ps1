<#
.SYNOPSIS
  Sends one task to a local Ollama model and writes the reply to a file. Used by the Orchestrator to delegate
  mechanical work (test drafts, translations, demo data) to the owner's machine instead of a cloud model.

.EXAMPLE
  pwsh scripts/ollama-task.ps1 -SystemFile brief/system.txt -PromptFile brief/prompt.txt -OutFile out/Tests.cs
  pwsh scripts/ollama-task.ps1 -Model qwen2.5-coder:7b -SystemFile s.txt -PromptFile p.txt -OutFile o.txt -NumCtx 8192

.NOTES
  Requires Ollama running on http://localhost:11434 with the model pulled (`ollama list`). Output is written as
  UTF-8 without BOM. The script prints one line of timing so the caller can judge the model's speed.
#>
param(
    [string]$Model = 'qwen2.5-coder:32b',
    [Parameter(Mandatory)] [string]$SystemFile,
    [Parameter(Mandatory)] [string]$PromptFile,
    [Parameter(Mandatory)] [string]$OutFile,
    [int]$NumCtx = 16384,
    [double]$Temperature = 0.2,
    [int]$TimeoutSec = 1800
)

$body = @{
    model    = $Model
    stream   = $false
    options  = @{ temperature = $Temperature; num_ctx = $NumCtx }
    messages = @(
        @{ role = 'system'; content = (Get-Content -Raw $SystemFile) },
        @{ role = 'user'; content = (Get-Content -Raw $PromptFile) })
} | ConvertTo-Json -Depth 6

$sw = [Diagnostics.Stopwatch]::StartNew()
$response = Invoke-RestMethod -Uri 'http://localhost:11434/api/chat' -Method Post `
    -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $TimeoutSec
$sw.Stop()

$content = $response.message.content
# Models sometimes wrap code in fences despite instructions; strip a single outer fence.
$content = [regex]::Replace($content, '^\s*```[a-zA-Z#+]*\s*\r?\n', '')
$content = [regex]::Replace($content, '\r?\n```\s*$', '')
[IO.File]::WriteAllText($OutFile, $content, [Text.UTF8Encoding]::new($false))

$tokPerSec = if ($response.eval_duration) { [math]::Round($response.eval_count / ($response.eval_duration / 1e9), 1) } else { 0 }
"model=$($response.model) seconds=$([int]$sw.Elapsed.TotalSeconds) prompt_tokens=$($response.prompt_eval_count) output_tokens=$($response.eval_count) tok/s=$tokPerSec out=$OutFile"
