{
  description = "REPONativeUltrawide — BepInEx mod dev shell (dotnet SDK for REPO modding)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = import nixpkgs { inherit system; };
    in
    {
      devShells.${system}.default = pkgs.mkShell {
        packages = [
          pkgs.dotnet-sdk_8
        ];

        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        DOTNET_NOLOGO = "1";

        shellHook = ''
          export DOTNET_ROOT="${pkgs.dotnet-sdk_8}"
          export PATH="$DOTNET_ROOT:$PATH"
          echo "REPONativeUltrawide dev shell — dotnet $(dotnet --version)"
          echo "Build:  dotnet build -c Release   (auto-deploys the dll into the Gale profile)"
        '';
      };
    };
}
