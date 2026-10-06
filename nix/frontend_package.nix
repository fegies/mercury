{ lib
, sources
, buildNpmPackage
}:
buildNpmPackage rec {
  pname = "mercury-frontend";
  version = "0.0.1";

  src = sources;

  npmDepsHash = "sha256-n6h7IMc34W6v7ZQ9Ibyr6tkXKSdUElMWebtmVgM3wKs=";

  npmPackFlags = [ "--ignore-scripts" ];

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
