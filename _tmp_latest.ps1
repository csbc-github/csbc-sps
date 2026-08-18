Set-Location $PSScriptRoot
$d = Get-ChildItem '.\ThreeDScan\export\SURF-CAM-01' -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 3
foreach ($x in $d) {
  Write-Host ("==== {0} {1} ====" -f $x.LastWriteTime.ToString('HH:mm:ss'), $x.Name)
  Get-ChildItem $x.FullName | ForEach-Object { Write-Host ("  {0,12} {1}" -f $_.Length, $_.Name) }
}
