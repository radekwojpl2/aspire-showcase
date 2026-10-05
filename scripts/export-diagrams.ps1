<#
.SYNOPSIS
    Puts the diagrams from docs/architecture/workspace.dsl into docs/architecture/arc42.md.

.DESCRIPTION
    Exports every view of the Structurizr workspace as Mermaid, which GitHub renders, and replaces
    each "<!-- diagram: <view key> -->" ... "<!-- /diagram -->" block in arc42.md with the current
    export of that view. Edit the model, not the diagrams, then run this again. Needs Docker.
#>
$ErrorActionPreference = 'Stop'

$architecture = (Resolve-Path (Join-Path $PSScriptRoot '..\docs\architecture')).Path
$document = Join-Path $architecture 'arc42.md'
$exported = Join-Path ([IO.Path]::GetTempPath()) "structurizr-mermaid-$([guid]::NewGuid())"
New-Item -ItemType Directory $exported | Out-Null

try {
    docker run --rm -v "${architecture}:/workspace:ro" -v "${exported}:/out" `
        structurizr/structurizr export -w /workspace/workspace.dsl -f mermaid -o /out | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Exporting the workspace failed; see the output above.' }

    $fence = '```'
    $missing = [Collections.Generic.List[string]]::new()
    $text = [IO.File]::ReadAllText($document) -replace "`r`n", "`n"
    $text = [regex]::Replace($text, '(?s)(<!-- diagram: (\w+) -->\n).*?(<!-- /diagram -->)', {
        param($match)
        $view = $match.Groups[2].Value
        $file = Join-Path $exported "structurizr-$view.mmd"
        if (-not (Test-Path $file)) {
            $missing.Add($view)
            return $match.Value
        }
        $mermaid = ([IO.File]::ReadAllText($file) -replace "`r`n", "`n").Trim()
        "$($match.Groups[1].Value)$fence" + "mermaid`n$mermaid`n$fence`n$($match.Groups[3].Value)"
    })
    if ($missing.Count -gt 0) { throw "No view with the key: $($missing -join ', ')." }

    [IO.File]::WriteAllText($document, $text, [Text.UTF8Encoding]::new($false))
    Write-Host "Updated the diagrams in $document."
}
finally {
    Remove-Item -Recurse -Force $exported
}
