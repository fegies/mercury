# A self-contained mock OpenID Connect provider for integration tests.
#
# oidc-provider-mock (https://github.com/geigerzaehler/oidc-provider-mock) is a
# small Python OpenID Provider built for testing. It is not (yet) packaged in
# the nixpkgs release this flake pins, so we vendor it here together with its
# only unpackaged dependency (htpy).
#
# Exposes a single output: `oidc-provider-mock` (the console script). It signs
# ID tokens with RS256, serves JWKS at /jwks and a discovery document at
# /.well-known/openid-configuration, and happily accepts any client id/secret
# unless --require-registration is passed.
{
  lib,
  python3,
  fetchurl,
}: let
  # Rebuild the python package set so our vendored packages participate in
  # dependency resolution normally.
  py = python3.override {
    packageOverrides = self: super: {
      htpy = self.buildPythonPackage {
        pname = "htpy";
        version = "26.5.1";
        format = "wheel";
        src = fetchurl {
          url = "https://files.pythonhosted.org/packages/e7/68/ad12d6519ccc48852ebb1effbe5a160ff21d7b48c484d0dae6fd173f6d22/htpy-26.5.1-py3-none-any.whl";
          sha256 = "sha256-FIVm251yDIl7qlgy4qSWlUzjF6sG70mhA7RPihUyLDs=";
        };
        propagatedBuildInputs = [self.markupsafe];
        pythonImportsCheck = ["htpy"];
        doCheck = false;
      };

      oidc-provider-mock = self.buildPythonPackage {
        pname = "oidc-provider-mock";
        version = "0.4.6";
        format = "wheel";
        src = fetchurl {
          url = "https://files.pythonhosted.org/packages/90/c3/8176f0052357be14af8e924d7eb6afad39846ec5b06fc213a12a82c3a0aa/oidc_provider_mock-0.4.6-py3-none-any.whl";
          sha256 = "sha256-Ull1c+C3tcBFzwJ0slkDJ5lR1WQcam6KxRkj00dDEAw=";
        };
        propagatedBuildInputs = with self; [
          authlib
          flask
          htpy
          httpx
          joserfc
          pydantic
          pyyaml
          uvicorn
        ];
        pythonImportsCheck = ["oidc_provider_mock"];
        doCheck = false;
      };
    };
  };
in
  py.pkgs.oidc-provider-mock
