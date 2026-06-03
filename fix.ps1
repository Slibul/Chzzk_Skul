$lines = [System.IO.File]::ReadAllLines('ChzzkGameMode.cs', [System.Text.Encoding]::UTF8)
$newLines = New-Object System.Collections.Generic.List[string]
for ($i=0; $i -lt $lines.Length; $i++) {
    if ($i -lt 941 -or $i -gt 976) {
        $newLines.Add($lines[$i])
    }
}
[System.IO.File]::WriteAllLines('ChzzkGameMode.cs', $newLines, [System.Text.Encoding]::UTF8)
