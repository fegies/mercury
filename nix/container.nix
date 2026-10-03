# OCI container image for Project Mercury.
#
# A single image containing all three production processes, started by a
# small entrypoint script under tini (PID 1):
#   - nginx reverse proxy (port 8080, the only exposed port)
#   - SvelteKit frontend (adapter-node, 127.0.0.1:3000)
#   - ASP.NET Core backend (127.0.0.1:5023)
#
# Build & load:
#   nix build .#container
#   docker load -i ./result   # or: podman load -i ./result
#
# Runtime contract (see docs/DEPLOYMENT.md for the full reference):
#   - Fully rootless: everything runs as the unprivileged user mercury
#     (uid 1000) — the image sets User=1000, no process ever runs as root.
#     Rootless podman/docker work (subuid/subgid assumed configured);
#     publishing the port is e.g. -p 8080:8080.
#   - /var/lib/mercury is declared as a VOLUME and pre-owned by uid 1000 in
#     the image; uploaded images live in data/images/{auctions,users}. Bind
#     mounts must be owned by uid 1000 on the host (chown 1000:1000), or use
#     --userns=keep-id on rootless podman.
#   - TLS is terminated upstream; the container speaks plain HTTP on :8080
#     and forwards X-Forwarded-* metadata at every level (nginx ->
#     node/frontend and nginx -> backend). The upstream proxy MUST set
#     X-Forwarded-Proto and Host.
#   - If any of the three services dies, the entrypoint stops the remaining
#     ones and exits, letting the container restart as a whole (use a
#     restart policy, e.g. podman run --restart=on-failure).
#   - No privileges required: no capabilities, no cgroup configuration.
#   - Configuration is passed as environment variables (e.g.
#     ConnectionStrings__DefaultConnection, OidcConfig__AuthorityUrl); see
#     docs/DEPLOYMENT.md.
{
  lib,
  bash,
  dockerTools,
  runCommand,
  writeText,
  tini,
  nginx,
  nodejs,
  coreutils,
  backend,
  frontend,
}:

let
  version = "0.0.1";
  stateDir = "/var/lib/mercury";

  nginxConf = writeText "nginx.conf" ''
    daemon off;
    worker_processes auto;
    pid /tmp/nginx.pid;
    error_log /dev/stderr warn;

    events {
      worker_connections 512;
    }

    http {
      include ${nginx}/conf/mime.types;
      default_type application/octet-stream;
      access_log /dev/stdout;
      sendfile on;
      keepalive_timeout 65;
      # Photo uploads are POSTed as multipart bodies and re-sent to the
      # backend by the SvelteKit server, so they traverse this proxy twice.
      # Aligned with ASP.NET's 30 MB default request size limit and the
      # adapter-node BODY_SIZE_LIMIT set in the image environment.
      client_max_body_size 30m;
      client_body_temp_path /tmp/nginx/client_body;
      proxy_temp_path /tmp/nginx/proxy;

      # The TLS terminator ahead of this container sends X-Forwarded-Proto
      # (https); pass it through unchanged. Requests that arrive without the
      # header (plain HTTP, e.g. health probes) fall back to the actual
      # scheme of the connection.
      map $http_x_forwarded_proto $forwarded_scheme {
        "" $scheme;
        default $http_x_forwarded_proto;
      }

      # Unprivileged port: everything in this container runs as uid 1000;
      # publish with -p 8080:8080 (or -p 80:8080 if the host side should be 80).
      server {
        listen 0.0.0.0:8080;

        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $forwarded_scheme;
        proxy_set_header X-Forwarded-Host $host;

      # Content-hashed build assets: served straight from the image store,
      # taking the load off the node server. Filenames change on every
      # build, so responses can be cached forever. The vite build emits
      # precompressed .gz variants alongside every asset; gzip_static serves
      # them without per-request compression (clients without
      # Accept-Encoding: gzip get the uncompressed originals).
      location /_app/immutable/ {
        root ${frontend}/build/client;
        add_header Cache-Control "public, max-age=31536000, immutable";
        gzip_static on;
        access_log off;
        try_files $uri =404;
      }

      # Backend (ASP.NET Core). The backend also emits
      # X-Accel-Buffering: no on the SSE stream, which nginx honors.
      location = /signin-oidc {
        proxy_pass http://127.0.0.1:5023;
      }
        location /api/ {
          proxy_pass http://127.0.0.1:5023;
          # Long-lived SSE connections must survive idle periods.
          proxy_read_timeout 1h;
        }

        # Frontend (SvelteKit adapter-node). The node server derives the
        # request origin from the X-Forwarded-Proto/Host headers set above,
        # and serves the styled /signedout landing page.
        location / {
          proxy_pass http://127.0.0.1:3000;
        }
      }
    }
  '';

  mercury-etc = runCommand "mercury-etc" { } ''
    mkdir -p $out/etc/nginx

    cp ${nginxConf} $out/etc/nginx/nginx.conf

    cat > $out/etc/passwd <<'EOF'
    root:x:0:0:System administrator:/root:/bin/false
    mercury:x:1000:1000:Project Mercury service user:/var/lib/mercury:/bin/false
    nobody:x:65534:65534:Kernel Overflow User:/nonexistent:/bin/false
    EOF

    cat > $out/etc/group <<'EOF'
    root:x:0:
    mercury:x:1000:
    nobody:x:65534:
    EOF

    cat > $out/etc/resolv.conf <<'EOF'
    nameserver 127.0.0.11
    EOF
  '';

  # Entrypoint: starts the three services; exits (killing the rest) when any
  # of them dies, so the container runtime can restart the stack as a whole.
  # tini (PID 1) forwards SIGTERM and reaps orphans. Everything runs as uid
  # 1000 (the image's User) — no privilege drops needed.
  entrypoint = runCommand "mercury-entrypoint" { } ''
    mkdir -p $out/bin
    cat > $out/bin/mercury-entrypoint <<EOF
    #!${bash}/bin/sh
    mkdir -p ${stateDir}/data/images ${stateDir}/tmp ${stateDir}/.aspnet/DataProtection-Keys
    mkdir -p /tmp/nginx/client_body /tmp/nginx/proxy

    ${nginx}/bin/nginx -c /etc/nginx/nginx.conf &
    ${nodejs}/bin/node ${frontend}/build/index.js &
    ${backend}/bin/webshell &

    shutdown() {
      kill \$(jobs -p) 2>/dev/null
      wait
      exit 0
    }
    trap shutdown TERM INT

    wait -n
    echo "a service exited; stopping the remaining services" >&2
    kill \$(jobs -p) 2>/dev/null
    wait
    exit 1
    EOF
    chmod +x $out/bin/mercury-entrypoint
  '';
in
dockerTools.buildLayeredImage {
  name = "mercury";
  tag = version;

  contents = [
    tini
    entrypoint
    mercury-etc
    nginx
    nodejs
    coreutils
    backend
    frontend
  ];

  extraCommands = ''
    # Directory skeleton, pre-owned by the service user so fresh named
    # volumes start out writable (bind mounts must be chowned to 1000:1000
    # on the host).
    mkdir -p root tmp var/lib/mercury/data/images var/lib/mercury/tmp \
             var/lib/mercury/.aspnet/DataProtection-Keys
  '';

  fakeRootCommands = ''
    chmod 1777 tmp
    chmod 700 root
    chown 1000:1000 var/lib/mercury var/lib/mercury/data/images var/lib/mercury/tmp \
      var/lib/mercury/.aspnet var/lib/mercury/.aspnet/DataProtection-Keys
  '';

  config = {
    Entrypoint = [
      "${tini}/bin/tini"
      "--"
      "${entrypoint}/bin/mercury-entrypoint"
    ];

    User = "1000";

    Env = [
      "PATH=/bin:/sbin"
      "HOME=${stateDir}"
      "NODE_ENV=production"
      "HOST=127.0.0.1"
      "PORT=3000"
      "PROTOCOL_HEADER=x-forwarded-proto"
      "HOST_HEADER=x-forwarded-host"
      "ASPNETCORE_ENVIRONMENT=Production"
      "ASPNETCORE_URLS=http://127.0.0.1:5023"
      "ImageStorage__BasePath=${stateDir}/data/images"
      "TMPDIR=${stateDir}/tmp"
      # Photo uploads traverse the node server (SSR re-sends them to the
      # backend); adapter-node defaults to 512 KiB. 30 MiB matches the
      # nginx client_max_body_size and ASP.NET's default request limit.
      "BODY_SIZE_LIMIT=30M"
    ];

    ExposedPorts = {
      "8080/tcp" = { };
    };

    Volumes = {
      ${stateDir} = { };
    };

    # Probes /api/healthz (the backend's anonymous liveness endpoint)
    # through the full proxy chain (nginx -> backend). node is already part
    # of the image, so this needs no extra dependency.
    Healthcheck = {
      Test = [
        "CMD"
        "${nodejs}/bin/node"
        "-e"
        "fetch('http://127.0.0.1:8080/api/healthz').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
      ];
      # Durations in the image config JSON are nanoseconds (docker's
      # Dockerfile "30s" syntax only applies at Dockerfile-parse time).
      Interval = 30000000000;
      Timeout = 5000000000;
      Retries = 3;
      StartPeriod = 30000000000;
    };

    Labels = {
      "org.opencontainers.image.title" = "mercury";
      "org.opencontainers.image.description" =
        "Project Mercury auction platform (frontend + backend + reverse proxy)";
      "org.opencontainers.image.version" = version;
      "org.opencontainers.image.licenses" = "MIT";
    };
  };

  meta = with lib; {
    description = "Project Mercury OCI container (frontend + backend + reverse proxy)";
    license = licenses.mit;
    platforms = platforms.linux;
  };
}
