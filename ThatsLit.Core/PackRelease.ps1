$from = $pwd
cd $PSScriptRoot
Get-Content ".\ThatsLitPlugin.cs" | Select-String 'public const string ModVersion = "([0-9.]+)"' | ForEach-Object {
    $v = $_.Matches[0].Groups[1].Value
  }
Write-Host($v)
Bandizip.exe c -root:BepInEx\plugins\ThatsLit "ThatsLit_${v}.zip" ..\..\..\BepInEx\plugins\ThatsLit\*.thatslitcompat.json ..\..\..\BepInEx\plugins\ThatsLit\*.md ..\..\..\BepInEx\plugins\ThatsLit\ThatsLit.Core.dll
cd $from