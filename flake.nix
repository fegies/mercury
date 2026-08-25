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
        server =
          pkgs.callPackage (import ./nix/backend_package.nix)
          {
            sources = ./backend;
          };
        frontend = pkgs.callPackage (import ./nix/frontend_package.nix) {sources = ./frontend;};
      });
  };
}
