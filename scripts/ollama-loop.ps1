<#
.SYNOPSIS
  Lets a local Ollama model implement a brief end to end: generate files, build with warnings-as-errors, run the
  given tests, and feed the compiler/test output back to the model for up to -MaxRounds rounds. The Orchestrator
  reads only the final summary, which keeps cloud-model usage to the brief and the review.

.EXAMPLE
  pwsh scripts/ollama-loop.ps1 -Root C:\ChorosApps\wt-x -SystemFile brief\sys.txt -PromptFile brief\task.txt `
      -BuildProjects tests/Salon.Tests.Unit -TestProject tests/Salon.Tests.Unit -TestFilter "FullyQualifiedName~Foo" -WorkDir brief\out

.NOTES
  The model must answer with one or more blocks, each starting with a line `// FILE: <path relative to Root>`.
  Files are written as UTF-8 without BOM. Nothing is committed; review the diff before committing.
#>
param(
    [Parameter(Mandatory)] [string]$Root,
    [Parameter(Mandatory)] [string]$SystemFile,
    [Parameter(Mandatory)] [string]$PromptFile,
    [string[]]$BuildProjects = @('tests/Salon.Tests.Unit'),
    [string]$TestProject = '',
    [string]$TestFilter = '',
    [string]$Model = 'qwen2.5-coder:32b',
    [int]$MaxRounds = 3,
    [int]$NumCtx = 16384,
    [string]$WorkDir = ''
)

$ErrorActionPreference = 'Stop'
$BuildProjects = @($BuildProjects | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) ("ollama-loop-" + [guid]::NewGuid().ToString('N').Substring(0, 8)) }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$system = Get-Content -Raw $SystemFile
$prompt = Get-Content -Raw $PromptFile
$messages = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $prompt })
$written = [System.Collections.Generic.List[string]]::new()
$summary = [ordered]@{ rounds = 0; files = @(); build = 'not run'; tests = 'not run'; lastErrors = @() }

function Invoke-Model($msgs) {
    $body = @{ model = $Model; stream = $false; options = @{ temperature = 0.2; num_ctx = $NumCtx }; messages = $msgs } | ConvertTo-Json -Depth 8
    $r = Invoke-RestMethod -Uri 'http://localhost:11434/api/chat' -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 1800
    return $r.message.content
}

function Write-Blocks($text) {
    $text = [regex]::Replace($text, '(?m)^\s*```[a-zA-Z#+]*\s*$', '')
    $matches = [regex]::Matches($text, '(?ms)^// FILE: (?<path>\S+)\s*\r?\n(?<body>.*?)(?=^// FILE: |\z)')
    foreach ($m in $matches) {
        $rel = $m.Groups['path'].Value.Trim()
        $full = Join-Path $Root $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
        [IO.File]::WriteAllText($full, $m.Groups['body'].Value.TrimEnd() + "`n", [Text.UTF8Encoding]::new($false))
        if (-not $written.Contains($rel)) { $written.Add($rel) }
    }
    return $matches.Count
}

function Get-BuildErrors {
    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $BuildProjects) {
        $out = & dotnet build (Join-Path $Root $p) -warnaserror --nologo -v q 2>&1 | Out-String
        foreach ($line in ($out -split "`r?`n")) {
            if ($line -match '(error [A-Z]+\d+|error WHITESPACE|error IMPORTS)') {
                $clean = ($line -replace '\s\[.*\]$', '') -replace [regex]::Escape($Root + '\'), ''
                if (-not $errors.Contains($clean)) { $errors.Add($clean) }
            }
        }
    }
    return $errors
}

function Get-TestFailures {
    if (-not $TestProject) { return @() }
    $args = @('test', (Join-Path $Root $TestProject), '--no-build', '--nologo', '-v', 'q')
    if ($TestFilter) { $args += @('--filter', $TestFilter) }
    $out = & dotnet @args 2>&1 | Out-String
    $script:testOutput = $out
    if ($out -match 'Passed!') { return @() }
    $lines = ($out -split "`r?`n") | Where-Object { $_ -match '\[FAIL\]|Error Message|Falsifiable|Shouldly|Expected|but was|Exception' } | Select-Object -First 40
    if (-not $lines) { $lines = @('tests did not report Passed!; output: ' + ($out.Substring(0, [Math]::Min(1500, $out.Length)))) }
    return $lines
}

for ($round = 1; $round -le $MaxRounds; $round++) {
    $summary.rounds = $round
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $reply = Invoke-Model $messages
    $sw.Stop()
    [IO.File]::WriteAllText((Join-Path $WorkDir "round-$round-reply.txt"), $reply, [Text.UTF8Encoding]::new($false))
    $count = Write-Blocks $reply
    Write-Host ("round {0}: {1} file block(s) in {2}s" -f $round, $count, [int]$sw.Elapsed.TotalSeconds)
    if ($count -eq 0) { $summary.lastErrors = @('model returned no // FILE: blocks'); break }

    & dotnet format (Join-Path $Root 'Salon.slnx') --include ($written | ForEach-Object { $_ }) 2>&1 | Out-Null
    $buildErrors = Get-BuildErrors
    if ($buildErrors.Count -gt 0) {
        $summary.build = "failed ($($buildErrors.Count) errors)"
        $summary.lastErrors = @($buildErrors | Select-Object -First 30)
        Write-Host ("round {0}: {1} build error(s)" -f $round, $buildErrors.Count)
    }
    else {
        $summary.build = 'ok'
        $failures = Get-TestFailures
        if ($failures.Count -eq 0) { $summary.tests = if ($TestProject) { 'ok' } else { 'not run' }; $summary.lastErrors = @(); break }
        $summary.tests = 'failed'
        $summary.lastErrors = @($failures)
        Write-Host ("round {0}: test failures" -f $round)
    }

    if ($round -eq $MaxRounds) { break }
    $current = ($written | ForEach-Object { "// FILE: $_`n" + (Get-Content -Raw (Join-Path $Root $_)) }) -join "`n"
    $feedback = "The build or tests failed. Fix the problems and output the COMPLETE corrected file(s) again, each starting with `// FILE: <path>`, no prose.`n`nErrors:`n" + ($summary.lastErrors -join "`n") + "`n`nCurrent files:`n" + $current
    $messages = @(@{ role = 'system'; content = $system }, @{ role = 'user'; content = $prompt }, @{ role = 'assistant'; content = $reply }, @{ role = 'user'; content = $feedback })
}

$summary.files = @($written)
$summary | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $WorkDir 'summary.json')
