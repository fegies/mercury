{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-26.05";
    systems.url = "github:nix-systems/default";
    devenv = {
      url = "github:cachix/devenv";
      inputs = {
        nixpkgs.follows = "nixpkgs";
      };
    };
  };

  nixConfig = {
    extra-trusted-public-keys = "devenv.cachix.org-1:w1cLUi8dv3hnoSPGAuibQv+f9TZLr6cv/Hm9XgU50cw=";
    extra-trusted-substituters = "https://devenv.cachix.org";
  };

  outputs = {
    self,
    nixpkgs,
    devenv,
    systems,
    ...
  } @ inputs: let
    forEachSystem = nixpkgs.lib.genAttrs (import systems);
  in {
    devShells =
      forEachSystem
      (system: let
        pkgs = nixpkgs.legacyPackages.${system};
      in {
        default = devenv.lib.mkShell {
          inherit inputs pkgs;
          modules = [
            {
              # https://devenv.sh/reference/options/
              packages = with pkgs; [
                nodejs_26
                prettier
                postgresql
                jq
              ];

              languages = {
                dotnet = {
                  enable = true;
                  package = pkgs.dotnet-sdk_10;
                };
              };

              services.postgres = {
                enable = true;
                package = pkgs.postgresql_18;
                initialDatabases = [
                  {name = "mercury";}
                ];
              };

              processes = {
                # backend.exec = "cd backend && export RUST_BACKTRACE=1 && exec cargo watch -x run";
                frontend.exec = "cd frontend && exec npm run dev";
              };
            }
          ];
        };
      });

    packages =
      forEachSystem
      (system: let
        pkgs = nixpkgs.legacyPackages.${system};
      in {
        devenv-up = self.devShells.${system}.default.config.procfileScript;

        # The API backend (ASP.NET Core).
        server = pkgs.callPackage (import ./nix/backend_package.nix) {
          sources = ./backend;
        };
        # alias matching the rest of the repo
        backend = self.packages.${system}.server;

        frontend = pkgs.callPackage (import ./nix/frontend_package.nix) {sources = ./frontend;};

        # Mock OpenID Connect provider used by the integration tests.
        oidc-provider-mock = pkgs.callPackage (import ./nix/oidc_mock.nix) {};

        # Regenerates nix/deps.json (nugetDeps for the backend) by restoring the
        # project with a network-enabled dotnet and converting the restored
        # packages to nixpkgs' nuget-to-json format. Run OUTSIDE the nix
        # builder sandbox (which has no network):
        #   nix run .#gen-deps -- backend/webshell/webshell.csproj nix/deps.json
        gen-deps = pkgs.writeShellScriptBin "gen-deps" ''
          set -euo pipefail
          export PATH=${pkgs.dotnet-sdk_10}/bin:${pkgs.nuget-to-json}/bin:$PATH
          work=$(mktemp -d)
          export DOTNET_CLI_HOME="$work/dotnet-home"
          export NUGET_PACKAGES="$work/restore-pkgs"
          mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
          trap 'rm -rf "$work"' EXIT
          project=''${1:?usage: gen-deps <csproj> <out.json>}
          out=''${2:?usage: gen-deps <csproj> <out.json>}
          dotnet restore "$project" -p:ContinuousIntegrationBuild=true -p:Deterministic=true >&2
          nuget-to-json "$NUGET_PACKAGES" > "$out"
        '';
      });

    tests =
      forEachSystem
      (system: let
        pkgs = nixpkgs.legacyPackages.${system};
      in {
        integration = import ./tests/integration.nix {
          inherit nixpkgs system;
          backend = self.packages.${system}.server;
          frontend = self.packages.${system}.frontend;
          oidc-provider-mock = self.packages.${system}.oidc-provider-mock;
        };
      });
  };
}
