# End-to-end integration test for Project Mercury built on the NixOS test
# framework.
#
# The test spins up a single NixOS VM (`mercury`) that runs:
#   - PostgreSQL (the "mercury" database)
#   - the ASP.NET Core backend (port 5023)
#   - the SvelteKit frontend (adapter-node, port 3000)
#   - a mock OpenID Connect provider (port 9400)
#   - an nginx reverse proxy (port 80) that routes /, /api, /signin-oidc and
#     /signedout exactly like a production deployment
#
# The test drives a real headless browser (Playwright + Chromium, both bundled
# by nixpkgs) from inside the VM. The browser script (integration_test.py)
# validates end-to-end against http://localhost:
#   1. everything started correctly
#   2. the OIDC login flow completes (a real browser follows every redirect)
#   3. an admin creates an auction (with a near-term closing time)
#   4. bids are placed by two bidders
#   5. live notifications work over the SSE stream through the nginx proxy:
#      the outbid bidder receives a toast and live price/badge updates, the
#      leader receives none, the winner receives a won toast when the auction
#      auto-closes, and bidders are notified when an admin cancels an auction
#
# Run with:
#   nix build .#tests.<system>.integration
{
  nixpkgs,
  system,
  backend,
  frontend,
  oidc-provider-mock,
}:
let
  pkgs = nixpkgs.legacyPackages.${system};

  # A self-contained Playwright driver. Installed as a binary on the test node
  # and executed inside the VM with `mercury.succeed`. The Chromium browser is
  # provided by nixpkgs' playwright-driver at PLAYWRIGHT_BROWSERS_PATH.
  browserTest = pkgs.writers.writePython3Bin "mercury-e2e" {
    libraries = [pkgs.python3Packages.playwright];
    # Playwright's fluent one-liners make the 79-column cap impractical; the
    # other flake8 checks still run.
    flakeIgnore = ["E501"];
  } (builtins.readFile ./integration_test.py);

  # Predefined users for the mock IdP. A generic `roles` claim conveys the
  # admin role (see GenericUserProvisioner).
  oidcUsers = pkgs.writeText "oidc-users.json" (builtins.toJSON [
    {
      sub = "admin";
      email = "admin@example.com";
      name = "Admin User";
      roles = [ "role.admin" ];
    }
    {
      sub = "bidder";
      email = "bidder@example.com";
      name = "Bidder";
    }
    {
      sub = "bidder2";
      email = "bidder2@example.com";
      name = "Bidder 2";
    }
  ]);
in
  nixpkgs.lib.nixos.runTest {
    name = "mercury-integration";
    hostPkgs = pkgs;
    node.pkgs = pkgs;
    # Only run on Linux; QEMU-based NixOS tests are not supported on macOS.
    meta.maintainers = [];

    nodes.mercury =
      {
        pkgs,
        lib,
        ...
      }: {
        # Use several vCPUs so the VM (and the browser) boot and run faster.
        virtualisation.cores = 8;
        virtualisation.memorySize = 4096;

        environment.systemPackages = [pkgs.postgresql browserTest];
        environment.variables.PLAYWRIGHT_BROWSERS_PATH = pkgs.playwright-driver.browsers;

        # ---- PostgreSQL -----------------------------------------------------
        services.postgresql = {
          enable = true;
          package = pkgs.postgresql_16;
          authentication = lib.mkForce ''
            local all all trust
            host  all all 127.0.0.1/32 trust
            host  all all ::1/128 trust
          '';
          initialScript = pkgs.writeText "init-mercury.sql" ''
            CREATE ROLE mercury WITH LOGIN PASSWORD 'mercury';
            CREATE DATABASE mercury OWNER mercury;
          '';
        };

        # ---- Mock OIDC provider ---------------------------------------------
        systemd.services.oidc-provider-mock = {
          description = "Mock OpenID Connect provider";
          wantedBy = ["multi-user.target"];
          after = ["network.target"];
          serviceConfig = {
            ExecStart = "${oidc-provider-mock}/bin/oidc-provider-mock -H 127.0.0.1 -p 9400 --user-claims-file ${oidcUsers}";
            DynamicUser = true;
            Restart = "on-failure";
          };
        };

        # ---- Backend ---------------------------------------------------------
        systemd.services.mercury-backend = {
          description = "Project Mercury API backend";
          wantedBy = ["multi-user.target"];
          after = ["postgresql.service" "oidc-provider-mock.service" "network.target"];
          requires = ["postgresql.service"];
          environment = {
            ASPNETCORE_URLS = "http://127.0.0.1:5023";
            ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=5432;Database=mercury;Username=mercury;Password=mercury";
            OidcConfig__AuthorityUrl = "http://127.0.0.1:9400";
            OidcConfig__ClientId = "mercury";
            OidcConfig__ClientSecret = "mercury";
            OidcConfig__ProviderType = "Generic";
          };
          serviceConfig = {
            ExecStart = "${backend}/bin/webshell";
            ExecStartPre = "${pkgs.postgresql}/bin/pg_isready -h 127.0.0.1 -p 5432";
            Restart = "on-failure";
            DynamicUser = true;
          };
        };

        # ---- Frontend ---------------------------------------------------------
        systemd.services.mercury-frontend = {
          description = "Project Mercury SvelteKit frontend";
          wantedBy = ["multi-user.target"];
          after = ["network.target"];
          environment = {
            HOST = "127.0.0.1";
            PORT = "3000";
            ORIGIN = "http://localhost";
            NODE_ENV = "production";
          };
          serviceConfig = {
            ExecStart = "${pkgs.nodejs_26}/bin/node ${frontend}/build/index.js";
            Restart = "on-failure";
            DynamicUser = true;
          };
        };

        # ---- Reverse proxy -----------------------------------------------------
        services.nginx = {
          enable = true;
          virtualHosts."localhost" = {
            listen = [
              {addr = "0.0.0.0"; port = 80;}
            ];
            locations = {
              "= /signin-oidc" = {
                proxyPass = "http://127.0.0.1:5023";
                extraConfig = "proxy_set_header Host $host;";
              };
              "= /signedout" = {
                proxyPass = "http://127.0.0.1:5023";
                extraConfig = "proxy_set_header Host $host;";
              };
              "/api/" = {
                proxyPass = "http://127.0.0.1:5023";
                extraConfig = "proxy_set_header Host $host;";
              };
              "/" = {
                proxyPass = "http://127.0.0.1:3000";
                extraConfig = "proxy_set_header Host $host;";
              };
            };
          };
        };
      };

    testScript = ''
      start_all()

      for unit in ["postgresql", "oidc-provider-mock", "mercury-backend", "mercury-frontend", "nginx"]:
          mercury.wait_for_unit(f"{unit}.service")

      # A unit being "active" only means it was launched; wait for the actual
      # sockets to be listening before driving the browser.
      mercury.wait_for_open_port(80)
      mercury.wait_for_open_port(9400)
      mercury.wait_for_open_port(5023)

      with subtest("end-to-end"):
          mercury.succeed("mercury-e2e")
    '';
  }
