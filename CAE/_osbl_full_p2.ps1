function Read-XlsxColumnar([string]$path) {
  $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
  try {
    $ss = @()
    $ssEntry = $zip.GetEntry('xl/sharedStrings.xml')
    if ($null -ne $ssEntry) { $sr = New-Object System.IO.StreamReader($ssEntry.Open(), [System.Text.Encoding]::UTF8); $xml = [xml]$sr.ReadToEnd(); $sr.Close(); $ss = @($xml.sst.si | ForEach-Object { $_.InnerText }) }
    $wsEntry = $zip.GetEntry('xl/worksheets/sheet1.xml')
    $sr2 = New-Object System.IO.StreamReader($wsEntry.Open(), [System.Text.Encoding]::UTF8)
    $wx = [xml]$sr2.ReadToEnd(); $sr2.Close()
    $rowsOut = New-Object 'System.Collections.Generic.List[object]'
    foreach ($row in $wx.worksheet.sheetData.row) {
      $rn = [int]$row.r; if ($rn -le 1) { continue }
      $cells = @{}; $ec = 1
      foreach ($c in $row.c) { $ref = [string]$c.r; $colIdx = 0
        if ($ref -ne '') { $letters = ($ref -replace '\d',''); $ci = 0; foreach ($ch in $letters.ToCharArray()) { $ci = $ci * 26 + ([int]$ch - 64) }; $colIdx = $ci } else { $colIdx = $ec }
        $ec = $colIdx + 1; $t = [string]$c.t; $val = ''
        if ($t -eq 's') { $val = $ss[[int]$c.v] } elseif ($t -eq 'inlineStr') { $val = [string]$c.is.InnerText } else { $val = [string]$c.v }
        $cells[$colIdx] = $val
      }
      $g = { param($ix) if ($cells.ContainsKey($ix)) { ([string]$cells[$ix]).Trim() } else { '' } }
      $rowsOut.Add([pscustomobject]@{ loop=(& $g 2); kks=(& $g 3); typno=(& $g 4); marka=(& $g 6); tipo=(& $g 7); name=(& $g 8); desc=(& $g 9); ctrl=(& $g 19); shab=(& $g 22) }) | Out-Null
    }
    return $rowsOut
  } finally { $zip.Dispose() }
}
$tbl = New-Object 'System.Collections.Generic.List[object]'
foreach ($fd in @(
  @{ file='C:\VSCode\CAE\OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_SCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file='C:\VSCode\CAE\OSBL_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_DCS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' },
  @{ file='C:\VSCode\CAE\OSBL_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx'; short='OSBL_GDS_ТЕХНОЛ_ОБЪЕКТЫ.xlsx' }
)) {
  $rowsOut = Read-XlsxColumnar $fd.file; $n = $rowsOut.Count
  $loopU = New-Object string[] $n; $loopNd = New-Object string[] $n; $namesU = New-Object string[] $n; $descsU = New-Object string[] $n; $wordSets = New-Object object[] $n
  $exAll = @{}; $exBaseAll = @{}
  for ($i = 0; $i -lt $n; $i++) {
    $r = $rowsOut[$i]; $loopU[$i] = $r.loop.ToUpperInvariant(); $loopNd[$i] = $loopU[$i] -replace '-',''
    $namesU[$i] = $r.name.ToUpperInvariant(); $descsU[$i] = $r.desc.ToUpperInvariant()
    $k = $loopU[$i]
    if ($k -ne '') { if (-not $exAll.ContainsKey($k)) { $exAll[$k] = New-Object 'System.Collections.Generic.List[int]' }; $exAll[$k].Add($i); $kb = $k -replace '-\d{1,2}$',''; if (-not $exBaseAll.ContainsKey($kb)) { $exBaseAll[$kb] = New-Object 'System.Collections.Generic.List[int]' }; $exBaseAll[$kb].Add($i) }
  }
  for ($i = 0; $i -lt $n; $i++) {
    $r = $rowsOut[$i]; $txt = (([string]$r.name) + ' ' + ([string]$r.desc)).ToLowerInvariant() -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*',' ' -replace '[^a-z0-9\-]+',' '
    $ws2 = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($w in ($txt -split '\s+')) { $w = $w.Trim('-'); if ($w.Length -ge 2 -and $w -notin @('the','and','of','in','on','at','for','with','from','by','or','an')) { [void]$ws2.Add($w) } }
    $wordSets[$i] = $ws2
  }
  $tbl.Add([pscustomobject]@{ short=$fd.short; n=$n; rows=$rowsOut; loopU=$loopU; loopNd=$loopNd; namesU=$namesU; descsU=$descsU; ws=$wordSets; exAll=$exAll; exBaseAll=$exBaseAll }) | Out-Null
  Write-Output ($fd.short + ' rows=' + $n)
}