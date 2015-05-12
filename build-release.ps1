# make sure the outercurve package is installed
libs\nuget.exe restore

# make sure that we don't have a test version registered in the registry
mkdir hkcu:software\Microsoft\PackageManagement -ea silentlycontinue
Set-ItemProperty -Path hkcu:software\Microsoft\PackageManagement -Name NuGet -Value $null

# call the MSBuild script
& "C:\Program Files (x86)\MSBuild\12.0\Bin\amd64\MSBuild.exe" .\NuGetProvider.sln /p:Configuration=Release