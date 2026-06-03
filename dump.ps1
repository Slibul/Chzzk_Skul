$asm = [Reflection.Assembly]::LoadFile("C:\Program Files (x86)\Steam\steamapps\common\Skul\Skul_Data\Managed\Assembly-CSharp.dll")
try { $types = $asm.GetTypes() } catch { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }
$types | Where-Object { $_.Name -match "DarkAbility|Tech" -or $_.FullName -match "DarkAbility|Tech" } | Select-Object FullName
