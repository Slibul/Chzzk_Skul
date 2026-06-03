$assembly = [System.Reflection.Assembly]::LoadFile("C:\Program Files (x86)\Steam\steamapps\common\Skul\Skul_Data\Managed\Assembly-CSharp.dll")
$types = $assembly.GetTypes()
$types | Where-Object { $_.Name -match 'Boss' -or $_.Name -match 'NPC' -or $_.Name -match 'Enemy' -or $_.Name -match 'Spawner' } | Select-Object Name | Select-Object -First 200 > types.txt
