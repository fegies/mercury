{pkgs, devenv, ...}:
devenv.lib.mkShell {
    packages = with pkgs; [
        nodejs_20
        nodePackages.prettier
        postgresql
        diesel-cli
        devenv
        cargo
        rustc
    ];

    services.postgresql.enable = true;
}