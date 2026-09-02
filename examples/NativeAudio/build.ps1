$cli = "..\..\Contract.Cli\bin\Debug\net10.0\Contract.Cli.exe"
$hostDll = "NativeAudioHost\bin\Debug\net10.0\NativeAudioHost.dll"

dotnet build NativeAudioHost\NativeAudioHost.csproj -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $cli --bind $hostDll NativeInterop.ct