$from = $pwd
cd $PSScriptRoot
Get-Content ".\ThatsLitPlugin.cs" | Select-String 'public const string ModVersion = "([0-9.]+)"' | ForEach-Object {
    $v = $_.Matches[0].Groups[1].Value
  }
[IO.File]::WriteAllLines((Resolve-Path ".update_notifier"), $v)
cd $from