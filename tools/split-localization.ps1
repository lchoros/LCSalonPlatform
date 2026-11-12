#!/usr/bin/env pwsh
<#
.SYNOPSIS
Splits a flat Localization/<culture>.json into one file per module: Localization/<culture>/<Module>.json.

.DESCRIPTION
ADR 0006 (amendment 2026-11-12): the host keeps one resource file per module and culture so that parallel slices do not
collide on a single en-GB.json / pl-PL.json. This script performs the split and is safe to re-run after merging a branch
that still appended keys to the flat files:

- the module of a key is its first dot segment ("Catalog.Services.Title" -> Catalog.json); a key without a dot goes to
  Common.json;
- the relative order of keys is preserved (no alphabetical re-sort); new keys are appended to the module file;
- a key that already exists in the module file is kept as it is. With -Overwrite the flat file's value wins instead.
  Either way, a value difference is reported;
- the flat file is deleted after a successful split unless -KeepFlat is given;
- a culture without a flat file is skipped, so running the script twice is a no-op.

Output files are UTF-8 without BOM, two-space indent, LF line endings, one "Key": "value" per line, final newline.

.PARAMETER LocalizationRoot
Folder holding <culture>.json and the <culture>/ folders. Defaults to src/Presentation/Salon.Web/Localization.

.PARAMETER Culture
Cultures to split. Defaults to en-GB and pl-PL.

.PARAMETER KeepFlat
Do not delete the flat <culture>.json after splitting.

.PARAMETER Overwrite
When a key exists in both the flat file and the module file with different text, take the flat file's text.

.EXAMPLE
pwsh tools/split-localization.ps1
Splits both cultures of the host and removes the flat files.

.EXAMPLE
pwsh tools/split-localization.ps1 -WhatIf
Shows which module files would change without writing anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $LocalizationRoot = (Join-Path $PSScriptRoot '..' 'src' 'Presentation' 'Salon.Web' 'Localization'),
    [string[]] $Culture = @('en-GB', 'pl-PL'),
    [switch] $KeepFlat,
    [switch] $Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$modulePattern = '^[A-Z][A-Za-z0-9]*$'
$defaultModule = 'Common'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

# Reads a flat "Key": "text" object preserving file order. Returns a list of [pscustomobject] @{ Key; Value }.
function Read-FlatJson([string] $Path) {
    $options = [System.Text.Json.JsonDocumentOptions] @{
        CommentHandling     = [System.Text.Json.JsonCommentHandling]::Skip
        AllowTrailingCommas = $true
    }
    # A Stream picks the single matching Parse overload; a byte[] is ambiguous for PowerShell.
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([System.IO.Stream] $stream, $options)
    }
    finally {
        $stream.Dispose()
    }
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "$Path must be a flat JSON object of key -> text."
        }

        $entries = [System.Collections.Generic.List[object]]::new()
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                throw "$Path key '$($property.Name)' must be a string, not $($property.Value.ValueKind)."
            }
            if (-not $seen.Add($property.Name)) {
                throw "$Path defines key '$($property.Name)' more than once."
            }
            $entries.Add([pscustomobject] @{ Key = $property.Name; Value = $property.Value.GetString() })
        }

        return , $entries
    }
    finally {
        $document.Dispose()
    }
}

function ConvertTo-FlatJson($Entries) {
    $lines = foreach ($entry in $Entries) {
        $key = [System.Text.Json.JsonEncodedText]::Encode([string] $entry.Key, $encoder).ToString()
        $value = [System.Text.Json.JsonEncodedText]::Encode([string] $entry.Value, $encoder).ToString()
        "  `"$key`": `"$value`""
    }

    return "{`n" + ($lines -join ",`n") + "`n}`n"
}

function Get-ModuleName([string] $Key) {
    $dot = $Key.IndexOf('.')
    $module = if ($dot -lt 0) { $defaultModule } else { $Key.Substring(0, $dot) }
    if ($module -notmatch $modulePattern) {
        throw "Key '$Key' has module '$module', which is not a PascalCase segment; fix the key before splitting."
    }
    return $module
}

$LocalizationRoot = [System.IO.Path]::GetFullPath($LocalizationRoot)
if (-not (Test-Path -LiteralPath $LocalizationRoot -PathType Container)) {
    throw "Localization root not found: $LocalizationRoot"
}

$differences = [System.Collections.Generic.List[string]]::new()

foreach ($cultureName in $Culture) {
    $flatPath = Join-Path $LocalizationRoot "$cultureName.json"
    if (-not (Test-Path -LiteralPath $flatPath -PathType Leaf)) {
        Write-Host "$cultureName`: no flat file at $flatPath; nothing to split."
        continue
    }

    $flatEntries = Read-FlatJson $flatPath
    $cultureFolder = Join-Path $LocalizationRoot $cultureName

    # Group by module in order of first appearance so a new module file reads top to bottom like the flat file did.
    $byModule = [System.Collections.Specialized.OrderedDictionary]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $flatEntries) {
        $module = Get-ModuleName $entry.Key
        if (-not $byModule.Contains($module)) {
            $byModule[$module] = [System.Collections.Generic.List[object]]::new()
        }
        $byModule[$module].Add($entry)
    }

    foreach ($module in $byModule.Keys) {
        $modulePath = Join-Path $cultureFolder "$module.json"
        # Assigned in two steps: an empty list emitted from an if-expression unrolls to $null in PowerShell.
        $existing = [System.Collections.Generic.List[object]]::new()
        if (Test-Path -LiteralPath $modulePath -PathType Leaf) {
            $existing = Read-FlatJson $modulePath
        }
        $index = @{}
        for ($i = 0; $i -lt $existing.Count; $i++) {
            $index[$existing[$i].Key] = $i
        }

        $added = 0
        $kept = 0
        $replaced = 0
        foreach ($entry in $byModule[$module]) {
            if ($index.ContainsKey($entry.Key)) {
                $current = $existing[$index[$entry.Key]]
                if ($current.Value -cne $entry.Value) {
                    $differences.Add("$cultureName/$module.json '$($entry.Key)': module file has '$($current.Value)', flat file has '$($entry.Value)'" + $(if ($Overwrite) { ' (overwritten)' } else { ' (kept module file)' }))
                    if ($Overwrite) {
                        $current.Value = $entry.Value
                        $replaced++
                        continue
                    }
                }
                $kept++
            }
            else {
                $existing.Add([pscustomobject] @{ Key = $entry.Key; Value = $entry.Value })
                $index[$entry.Key] = $existing.Count - 1
                $added++
            }
        }

        $text = ConvertTo-FlatJson $existing
        $unchanged = (Test-Path -LiteralPath $modulePath -PathType Leaf) -and ([System.IO.File]::ReadAllText($modulePath, $utf8NoBom) -ceq $text)
        if ($unchanged) {
            Write-Host "$cultureName/$module.json: unchanged ($kept kept)"
            continue
        }

        if ($PSCmdlet.ShouldProcess($modulePath, "write $added new, $kept kept, $replaced replaced")) {
            New-Item -ItemType Directory -Path $cultureFolder -Force | Out-Null
            [System.IO.File]::WriteAllText($modulePath, $text, $utf8NoBom)
            Write-Host "$cultureName/$module.json: $added added, $kept kept, $replaced replaced"
        }
    }

    if (-not $KeepFlat -and $PSCmdlet.ShouldProcess($flatPath, 'delete flat file')) {
        Remove-Item -LiteralPath $flatPath
        Write-Host "$cultureName`: removed $flatPath"
    }
}

if ($differences.Count -gt 0) {
    Write-Warning ("{0} key(s) had different text in the flat file and the module file:`n  {1}" -f $differences.Count, ($differences -join "`n  "))
}
