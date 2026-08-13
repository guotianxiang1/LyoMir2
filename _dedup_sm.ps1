function Remove-Members($path, $names){
  $lines = [System.Collections.Generic.List[string]](Get-Content -LiteralPath $path -Encoding UTF8)
  foreach($name in $names){
    $sig = -1
    for($i=0;$i -lt $lines.Count;$i++){
      $ln=$lines[$i]
      if($name -like 'BuildSm*'){ if($ln -match [regex]::Escape($name+'(')){ $sig=$i; break } }
      elseif($name -eq 'Sm966Body'){ if($ln -match 'Sm966Body' -and $ln -match '=' -and $ln -notmatch 'Clone'){ $sig=$i; break } }
    }
    if($sig -lt 0){ Write-Host "NOT FOUND: $name"; continue }
    $start=$sig; $j=$sig-1
    while($j -ge 0 -and $lines[$j].Trim().StartsWith('//')){ $start=$j; $j-- }
    $brace=0; $seen=$false; $end=-1; $k=$sig
    while($k -lt $lines.Count){
      foreach($ch in $lines[$k].ToCharArray()){
        if($ch -eq '{'){ $brace++; $seen=$true } elseif($ch -eq '}'){ $brace-- }
      }
      if($seen){ if($brace -eq 0){ $end=$k; break } }
      else { if($lines[$k].Contains(';')){ $end=$k; break } }
      $k++
    }
    if($end -lt 0){ Write-Host "NO END: $name"; continue }
    $e=$end
    if($e+1 -lt $lines.Count -and $lines[$e+1].Trim() -eq ''){ $e++ }
    $lines.RemoveRange($start, $e-$start+1)
    Write-Host ("removed {0}  (was near line {1})" -f $name, ($sig+1))
  }
  Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
}

$root='D:\loym2\.claude\worktrees\pois11-fix'
Remove-Members "$root\GameSvr\Actors\TBaseObject.SmIdent_Sm1.cs" @('Sm966Body','BuildSm966')
Remove-Members "$root\GameSvr\Actors\TBaseObject.SmIdent_Sm2.cs" @('BuildSm689','BuildSm951','BuildSm959','BuildSm965','BuildSm1201','BuildSm1250','BuildSm1251','BuildSm1252','BuildSm1253','BuildSm1254','BuildSm1255','BuildSm1256')
