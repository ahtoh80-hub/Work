function Get-FuncCode([string]$kksU) { $s = ([string]$kksU) -replace '^\d{4}-',''; $d = $s.IndexOf('-'); if ($d -gt 0) { return $s.Substring(0,$d) }; return $s }
function Get-LoopNumber([string]$kksU) { $s = ([string]$kksU) -replace '^\d{4}-',''; if ($s.IndexOf('-') -gt 0) { $s = $s.Substring($s.IndexOf('-')+1) }; if ($s.IndexOf('-') -ge 0) { $s = $s.Substring(0,$s.IndexOf('-')) }; $d = ($s -replace '\D','').TrimStart('0'); if ($d -eq '') { $d = '0' }; return $d }
$matchMap = @{}
foreach ($i in $items) {
  $sysU = ([string]$i.sysTag).Trim().ToUpperInvariant()
  $isDCSOnly = ($sysU -match '^\d{4}-I-')
  $tblActive = if ($isDCSOnly) { @($tbl | Where-Object { $_.short -like '*DCS*' }) } else { $tbl.ToArray() }
  $causeTag = ([string]$i.tagEN).Trim().ToUpperInvariant()
  if ($causeTag -ne '' -and $causeTag -ne ([char]0x2014)) {
    $key = $causeTag + '||' + $sysU
    $m = $null
    foreach ($t in $tblActive) { if ($t.exAll.ContainsKey($causeTag)) { $m = @{ i=[int]($t.exAll[$causeTag][0]); t=$t; sposob='Loop (точное совпадение)' }; break } }
    if ($null -eq $m) {
      $tc = Get-FuncCode $causeTag
      foreach ($t in $tblActive) {
        if (-not $isDCSOnly -and $t.short -like '*GDS*' -and $tc -notmatch '^G') { continue }
        for ($ii = 0; $ii -lt $t.n; $ii++) {
          if ($t.namesU[$ii].Contains($causeTag)) {
            $lu = $t.loopU[$ii]; $lc = Get-FuncCode $lu
            if (($lc.Substring(0,1) -eq $tc.Substring(0,1)) -or (($tc -in @('K','P','E')) -and $lc.StartsWith('M'))) { $m = @{ i=$ii; t=$t; sposob='Наименование (вхождение)' }; break }
          }
        }
        if ($null -ne $m) { break }
      }
    }
    if ($null -eq $m -and $i.descEN) {
      $dw = New-Object 'System.Collections.Generic.HashSet[string]'
      $dt = ([string]$i.descEN).ToLowerInvariant() -replace '\d{4}-[a-z0-9]+(?:-[a-z0-9]+)*',' ' -replace '[^a-z0-9\-]+',' '
      foreach ($w in ($dt -split '\s+')) { $w = $w.Trim('-'); if ($w.Length -ge 2 -and $w -notin @('the','and','of','in','on','at','for','with','from','by','or','an')) { [void]$dw.Add($w) } }
      if ($dw.Count -ge 3) {
        $bR = 0.0; $bT = $null; $bI = -1
        foreach ($t in $tblActive) { for ($ii = 0; $ii -lt $t.n; $ii++) { if (-not $t.loopU[$ii].StartsWith($causeTag.Substring(0,4))) { continue }; $cd = $t.ws[$ii]; if ($null -eq $cd -or $cd.Count -eq 0) { continue }; $itr = 0; foreach ($w in $dw) { if ($cd.Contains($w)) { $itr++ } }; $mx = [Math]::Max($dw.Count, $cd.Count); if ($mx -gt 0) { $rr = $itr / $mx; if ($itr -ge 3 -and $rr -gt $bR) { $bR = $rr; $bT = $t; $bI = $ii } } } }
        if ($null -ne $bT -and $bR -ge 0.6) { $pct = [int][Math]::Round($bR * 100); $m = @{ i=$bI; t=$bT; sposob=('Сходство описания (' + $pct + '%)') } }
      }
    }
    if ($null -ne $m) { $row = $m.t.rows[$m.i]; $matchMap[$key] = [ordered]@{ loop=$row.loop; typno=$row.typno; marka=$row.marka; name=$row.name; tipo=$row.tipo; shab=$row.shab; ctrl=$row.ctrl; file=$m.t.short; sposob=$m.sposob } } else { $matchMap[$key] = $null }
  }
  foreach ($e in $i.effects) {
    $effTag = ([string]$e.tag).Trim().ToUpperInvariant()
    if ($effTag -eq '' -or $effTag -eq ([char]0x2014)) { continue }
    $key = $effTag + '||' + $sysU
    if ($matchMap.ContainsKey($key)) { continue }
    $m = $null
    foreach ($t in $tblActive) { if ($t.exAll.ContainsKey($effTag)) { $m = @{ i=[int]($t.exAll[$effTag][0]); t=$t; sposob='Loop (точное совпадение)' }; break } }
    if ($null -ne $m) { $row = $m.t.rows[$m.i]; $matchMap[$key] = [ordered]@{ loop=$row.loop; typno=$row.typno; marka=$row.marka; name=$row.name; tipo=$row.tipo; shab=$row.shab; ctrl=$row.ctrl; file=$m.t.short; sposob=$m.sposob } } else { $matchMap[$key] = $null }
  }
}
$fc = 0; foreach ($v in $matchMap.Values) { if ($null -ne $v) { $fc++ } }
Write-Output ("matchMap: " + $matchMap.Count + " found: " + $fc)