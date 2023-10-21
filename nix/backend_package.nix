{ lib
, openssl
, pkg-config
, rustPlatform
, mold
, postgresql
, sources
}:
rustPlatform.buildRustPackage rec {
  pname = "projectMercury";
  version = "0.0.1";
  src = sources;
  cargoHash = "sha256-hOaCcvQxlI2cDl9GlacLJBywcUqsMcAfQw5n0zRt0WU=";

  nativeBuildInputs = [ openssl pkg-config mold postgresql ];

  env = {
    "PKG_CONFIG_PATH" = "${openssl.dev}/lib/pkgconfig";
  };
}
