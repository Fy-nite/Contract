$rids = "win-x64","linux-x64","linux-arm64","osx-x64","osx-arm64"
foreach($rid in $rids) {
  dotnet publish Contract.Cli\Contract.Cli.csproj -c Release -r $rid --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true -o "publish/$rid"
}
foreach ($item in Get-ChildItem publish) { Compress-Archive -Path "publish/$($item.name)" -DestinationPath "$($item.name).zip" }