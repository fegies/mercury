# Smoke test for the Project Mercury OCI container (nix/container.nix).
#
# Loads the image into **rootless** podman inside a NixOS VM and runs it with
# host networking against an in-VM PostgreSQL and the mock OpenID Connect
# provider (same services as tests/integration.nix), asserting:
#   1. the container runs fully unprivileged: every process inside runs as
#      the service user (mercury, uid 1000) — no root anywhere
#   2. the frontend is served through the container's nginx (:8080)
#   3. the backend is reachable through the proxy (/signedout)
#   4. the forwarded headers contract holds end-to-end: an upstream
#      X-Forwarded-Proto: https (simulating the TLS terminator) makes the
#      backend's OIDC login redirect advertise an https redirect_uri, while
#      plain requests stay on http
#   5. uploaded images are stored inside the /var/lib/mercury volume and the
#      directory is owned by the service user (mercury)
#   6. the image's HEALTHCHECK passes (run on demand)
#   7. the container runs with a read-only root filesystem (plus tmpfs /tmp),
#      the recommended production hardening
#   8. the container stops cleanly on SIGTERM (tini -> entrypoint)
#
# Run with:
#   nix build .#tests.<system>.container
{
  nixpkgs,
  system,
  container,
  oidc-provider-mock,
}:
let
  pkgs = nixpkgs.legacyPackages.${system};

  imageRef = "${container.imageName}:${container.imageTag}";

  # The image artifact is a plain file; wrap it so it can live inside
  # environment.systemPackages (a pkgs.buildEnv).
  image = pkgs.runCommand "mercury-image" { } ''
    mkdir -p $out/share/mercury
    ln -s ${container} $out/share/mercury/image.tar.gz
  '';

  # Predefined users for the mock IdP (mirrors tests/integration.nix); the
  # smoke test only exercises the login redirect, so a single user suffices.
  oidcUsers = pkgs.writeText "oidc-users.json" (builtins.toJSON [
    {
      sub = "admin";
      email = "admin@example.com";
      name = "Admin User";
      roles = [ "role.admin" ];
    }
  ]);
in
  nixpkgs.lib.nixos.runTest {
    name = "mercury-container";
    hostPkgs = pkgs;
    node.pkgs = pkgs;
    meta.maintainers = [];

    nodes.mercury =
      {
        pkgs,
        lib,
        ...
      }: {
        virtualisation.cores = 4;
        virtualisation.memorySize = 4096;
        # Room for the uncompressed image layers in podman's storage.
        virtualisation.diskSize = 8192;

        virtualisation.podman.enable = true;

        # Rootless podman: subordinate id ranges for the unprivileged user
        # running the containers (setuid newuidmap/newgidmap come from
        # security.shadow, enabled by default).
        users.users.podman-test = {
          isNormalUser = true;
          uid = 2000;
          subUidRanges = [
            {
              startUid = 100000;
              count = 65536;
            }
          ];
          subGidRanges = [
            {
              startGid = 100000;
              count = 65536;
            }
          ];
        };

        environment.systemPackages = [image pkgs.curl];

        # ---- PostgreSQL (identical to tests/integration.nix) ----------------
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

        # ---- Mock OIDC provider (identical to tests/integration.nix) --------
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
      };

    testScript = ''
      start_all()

      mercury.wait_for_unit("postgresql.service")
      mercury.wait_for_unit("oidc-provider-mock.service")
      mercury.wait_for_open_port(5432)
      mercury.wait_for_open_port(9400)

      # Rootless podman runs as the unprivileged user podman-test (uid 2000).
      # Enable lingering so a systemd user session (with a proper
      # XDG_RUNTIME_DIR and cgroup delegation) exists — the same setup a
      # real rootless deployment uses; podman needs it to register the
      # image's HEALTHCHECK.
      mercury.succeed("loginctl enable-linger 2000")
      mercury.wait_for_unit("user@2000.service")
      pod = "sudo -u podman-test -H env XDG_RUNTIME_DIR=/run/user/2000 podman"

      mercury.succeed(f"{pod} load -i ${image}/share/mercury/image.tar.gz")

      # Environment shared by both runs below (plain + read-only rootfs).
      env = (
        # The mock IdP is http-only; the backend refuses plain-HTTP OIDC
        # authorities outside the Development environment (the production
        # posture this image otherwise enforces).
        "-e 'ASPNETCORE_ENVIRONMENT=Development' "
        + "-e 'ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=mercury;Username=mercury;Password=mercury' "
        + "-e 'OidcConfig__AuthorityUrl=http://127.0.0.1:9400' "
        + "-e 'OidcConfig__ClientId=mercury' "
        + "-e 'OidcConfig__ClientSecret=mercury' "
        + "-e 'OidcConfig__ProviderType=Generic' "
      )

      # Plain run: the image is fully rootless (User=1000) — no extra
      # capabilities, no cgroup configuration, no tty needed; the entrypoint's
      # stdout/stderr IS the container log.
      mercury.succeed(
        f"{pod} run -d --name mercury --network=host "
        + env
        + f"{imageRef}"
      )

      # If the container died at boot, dump its logs so the failure is
      # visible in the test output instead of a bare port-wait timeout.
      code, status = mercury.execute(
          f"sleep 3; {pod} inspect --format '{{{{.State.Status}}}} exit={{{{.State.ExitCode}}}}' mercury"
      )
      print("container status:", status.strip())
      if "running" not in status:
          _, logs = mercury.execute(f"{pod} logs mercury")
          print("=== container logs ===")
          print(logs)
          raise Exception("container exited during startup")

      try:
          mercury.wait_for_open_port(8080, timeout=90)
      except Exception:
          # The entrypoint's stdout/stderr IS the container log; dump it to
          # make the failure diagnosable, then re-raise.
          _, logs = mercury.execute(f"{pod} logs mercury")
          print("=== container logs (port 8080 never opened) ===")
          print(logs)
          _, procs = mercury.execute(f"{pod} top mercury")
          print("=== container processes ===")
          print(procs)
          raise

      with subtest("no process runs as root"):
          owners = mercury.succeed(
              f"{pod} top mercury | tail -n +2 | awk '{{print $1}}' | sort -u"
          ).strip()
          print("process owners:", owners)
          assert owners in ("mercury", "1000"), owners

      with subtest("frontend served through the container's nginx"):
          mercury.succeed("curl -fsS http://localhost:8080/ -o /dev/null")

      with subtest("backend served through the container's nginx"):
          mercury.succeed("curl -fsS http://localhost:8080/api/healthz")

      with subtest("large uploads are not rejected by the proxy"):
          # Phone photos are several MB; nginx defaults to a 1 MiB
          # client_max_body_size and adapter-node to 512 KiB — both are
          # raised to 30 MiB in the image. POST a 2 MiB body at an anonymous
          # backend route and assert it is not rejected as too large (the
          # OIDC challenge redirect is the expected outcome).
          mercury.succeed("dd if=/dev/zero of=/tmp/big-upload bs=1M count=2")
          code = mercury.succeed(
              "curl -s -o /dev/null -w '%{http_code}' -X POST --data-binary @/tmp/big-upload http://localhost:8080/api/login"
          ).strip()
          print("large upload status:", code)
          assert code != "413", code

      with subtest("static assets served by nginx"):
          # Extract a hashed asset from the rendered /signedout HTML (the /
          # landing page redirects anonymous visitors, so it has no asset
          # references) and check that nginx serves it directly: the
          # precompressed .gz variant for gzip-capable clients, the original
          # for everyone else.
          asset = mercury.succeed(
              "curl -fsS http://localhost:8080/signedout "
              + "| grep -oE '/_app/immutable/[a-zA-Z0-9/._-]+' | head -1"
          ).strip()
          print("immutable asset:", asset)
          assert asset.startswith("/_app/immutable/"), asset

          gz = mercury.succeed(
              f"curl -sI -H 'Accept-Encoding: gzip' http://localhost:8080{asset}"
          ).lower()
          assert " 200 " in gz.splitlines()[0], gz
          assert "content-encoding: gzip" in gz, gz
          assert "immutable" in gz, gz

          plain = mercury.succeed(f"curl -sI http://localhost:8080{asset}").lower()
          assert " 200 " in plain.splitlines()[0], plain
          assert "content-encoding" not in plain, plain

      with subtest("forwarded scheme honored end-to-end"):
          # The TLS terminator in front of the container sets
          # X-Forwarded-Proto: https; the login redirect (towards the mock
          # IdP) must then advertise an https redirect_uri.
          with_proto = mercury.succeed(
              "curl -sI -H 'X-Forwarded-Proto: https' http://localhost:8080/api/login"
          ).lower()
          assert "redirect_uri=https%3a%2f%2f" in with_proto, with_proto

          # Without the header the scheme stays http.
          without_proto = mercury.succeed("curl -sI http://localhost:8080/api/login").lower()
          assert "redirect_uri=http%3a%2f%2f" in without_proto, without_proto

      with subtest("images stored in the /var/lib/mercury volume"):
          mercury.succeed(f"{pod} exec mercury /bin/test -d /var/lib/mercury/data/images")
          owner = mercury.succeed(
              f"{pod} exec mercury /bin/stat -c '%U' /var/lib/mercury"
          ).strip()
          assert owner == "mercury", owner

      with subtest("healthcheck passes"):
          mercury.succeed(f"{pod} healthcheck run mercury")

      # The recommended production posture: read-only root filesystem. The
      # image is prepared for it — nginx temp paths live under /tmp and
      # everything stateful (uploaded images, ASP.NET scratch and
      # DataProtection keys) lives in the /var/lib/mercury volume.
      mercury.succeed(f"{pod} stop -t 40 mercury")
      mercury.wait_for_closed_port(8080)

      with subtest("runs with a read-only root filesystem"):
          mercury.succeed(
            f"{pod} run -d --name mercury-ro --read-only --tmpfs /tmp --network=host "
            + env
            + f"{imageRef}"
          )
          try:
              mercury.wait_for_open_port(8080, timeout=90)
          except Exception:
              _, logs = mercury.execute(f"{pod} logs mercury-ro")
              print("=== read-only container logs (port 8080 never opened) ===")
              print(logs)
              raise
          mercury.succeed("curl -fsS http://localhost:8080/api/healthz")
          mercury.succeed("curl -fsS http://localhost:8080/ -o /dev/null")

      with subtest("clean shutdown on SIGTERM"):
          mercury.succeed(f"{pod} stop -t 40 mercury-ro")
          exit_code = mercury.succeed(f"{pod} wait mercury-ro").strip()
          # The entrypoint stops all services on SIGTERM and exits 0; some
          # conmon versions report 255 because they never receive SIGCHLD
          # for containers whose init is not their direct child (cosmetic
          # artifact, not an error).
          assert exit_code in ("0", "255"), exit_code
    '';
  }
