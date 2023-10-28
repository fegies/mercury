{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-23.05";
    systems.url = "github:nix-systems/default";
    fenix = {
      url = "github:nix-community/fenix";
      inputs.nixpkgs.follows = "nixpkgs";
    };
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

  outputs = { self, nixpkgs, devenv, systems, fenix, ... } @ inputs:
    let
      forEachSystem = nixpkgs.lib.genAttrs (import systems);
    in
    {
      devShells = forEachSystem
        (system:
          let
            pkgs = nixpkgs.legacyPackages.${system};
          in
          {
            default = devenv.lib.mkShell {
              inherit inputs pkgs;
              modules = [
                {
                  # https://devenv.sh/reference/options/
                  packages = with pkgs; [
                    nodejs_20
                    nodePackages.prettier
                    postgresql
                    diesel-cli
                    rustfmt
                    gcc
                    cargo-watch
                    mold
                    openssl
                  ];

                  languages.rust = {
                    enable = true;
                    channel = "stable";
                    components = [ "rustc" "cargo" "clippy" "rustfmt" "rust-analyzer" "rust-src" ];
                  };

                  enterShell = ''
                    pg_url="postgres://$(whoami)@$(readlink -f ./.devenv/state/postgres | jq -rR '.|@uri')/mercury"
                    (
                      echo "DATABASE_URL=$pg_url"
                      echo "FRONTEND_DIR=../frontend/build"
                      echo "ADMIN_PASSWORD=devpw"
                      echo "COOKIE_KEY=devkey"
                    ) > backend/.env
                  '';

                  services.postgres = {
                    enable = true;
                    initialDatabases = [
                      { name = "mercury"; }
                    ];
                  };

                  processes = {
                    backend.exec = "cd backend && export RUST_BACKTRACE=1 && exec cargo watch -x run";
                    frontend.exec = "cd frontend && exec npm run dev";
                  };
                }
              ];
            };
          });
      packages = forEachSystem
        (system:
          let pkgs = nixpkgs.legacyPackages.${system}; in
          {
            server = pkgs.callPackage (import ./nix/backend_package.nix)
              {
                sources = ./backend;
              };
            frontend = pkgs.callPackage (import ./nix/frontend_package.nix) { sources = ./frontend; };
          });
    };
}
