{
    lib,
    stdenvNoCC,
    fetchFromGitHub,
    buildDotnetModule,
    glibcLocales,
    dotnetCorePackages
}:
buildDotnetModule (finalAttrs: {
  pname = "contract";
  version = "1.0";

#  src = fetchFromGitHub {
#    owner = "Fy-nite";
#    repo = "Contract";
#    tag = "V${finalAttrs.version}B2";
#    hash = "sha256-vnjBhu9XFxPAOKz7aKRtxrCve1akXrmxXq1bS7kXneM=";
#    fetchSubmodules = true;
#  };
  src = ../.;

  dotnetRestoreFlags = "-p:TargetFramework=net10.0";

  # https://github.com/NixOS/nixpkgs/issues/38991
  # bash: warning: setlocale: LC_ALL: cannot change locale (en_US.UTF-8)
  env.LOCALE_ARCHIVE = lib.optionalString stdenvNoCC.hostPlatform.isLinux "${glibcLocales}/lib/locale/locale-archive";

  dotnet-sdk = dotnetCorePackages.sdk_10_0;

  projectFile = "Contract.Cli/Contract.Cli.csproj";

  nugetDeps = ./deps.json;
})
