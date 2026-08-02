param(
    [string]$version = "1.0.0"
)

# build and produce nupkg (will appear in ./nupkgs)
dotnet pack -c Release Contract.Cli\Contract.Cli.csproj
# remove previous global install if exists
dotnet tool uninstall --global cclc
# install from local folder (tool manifest not required for global install)
dotnet tool install --global --add-source ./Contract.Cli/bin/Release --version $version cclc