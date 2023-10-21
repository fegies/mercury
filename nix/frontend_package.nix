{ lib
, sources
, buildNpmPackage
}:
buildNpmPackage rec {
  pname = "projectpsi-frontend";
  version = "0.0.1";

  src = sources;

  npmDepsHash = "sha256-wRGS9N7R86nntStfRx13xC/BRx0ntnpbU32jLk4Dsyc=";

  npmPackFlags = [ "--ignore-scripts" ];
}
