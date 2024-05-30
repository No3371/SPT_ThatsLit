$from = $pwd
cd $PSScriptRoot
Get-Content ".\ThatsLitSyncPlugin.cs" | Select-String 'public const string ModVersion = "([0-9.]+)"' | ForEach-Object {
    $v = $_.Matches[0].Groups[1].Value
  }
Write-Host($v)
Bandizip.exe c -root:BepInEx\plugins\ThatsLit "ThatsLitSync_${v}.zip" ..\..\..\BepInEx\plugins\ThatsLit\ThatsLit.Sync.dll
cd $from