{
  lib,
  sources,
  buildNpmPackage,
}:
buildNpmPackage {
  pname = "mercury-frontend";
  version = "0.0.1";

  src = sources;

  npmDepsHash = "sha256-+fy2DI4mJet9Pp5Fl3mFw5Ed7yLkc1DUOoHrA1XhjGs=";

  npmPackFlags = ["--ignore-scripts"];

  # The default install phase only copies what `npm pack` would publish, which
  # excludes the gitignored `build/` output of `vite build` (adapter-node). We
  # keep just the built server + bundle so `node build/index.js` can run it.
  installPhase = ''
    runHook preInstall
    mkdir -p $out
    cp -r package.json build "$out/"
    runHook postInstall
  '';
}
