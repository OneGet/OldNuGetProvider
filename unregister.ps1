# make sure that we don't have a test version registered in the registry
mkdir hkcu:software\Microsoft\PackageManagement -ea silentlycontinue
Set-ItemProperty -Path hkcu:software\Microsoft\PackageManagement -Name NuGet -Value $null
