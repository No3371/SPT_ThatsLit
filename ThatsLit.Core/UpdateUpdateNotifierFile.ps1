$from = $pwd
cd $PSScriptRoot
Get-Content ".\ThatsLitPlugin.cs" | Select-String 'public const string ModVersion = "([0-9.]+)"' | ForEach-Object {
    $v = $_.Matches[0].Groups[1].Value
  }
$v | Out-File -FilePath ".update_notifier" -Encoding utf8 -Force
cd $from