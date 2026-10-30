#!/usr/bin/env python3
"""Mints a throwaway HS256 JWT to exercise CashFlowEntryService/ConsolidationService locally.

There is no Identity Provider in this challenge (ADR-007) — both services validate tokens
signed with a shared, publicly-documented dev-only key (see docker-compose.yml / appsettings.json,
`Jwt:SigningKey`). This script is the "token de desenvolvimento" ADR-007 asks for; it must never
be pointed at anything but a local demo.

Both services share a single `audience` (`cashflow-api`, ADR-007); authorization is scope-based,
not role-based — the `--scope` flag controls what a token is allowed to do. The default mints all
three scopes, for local convenience when you do not need to exercise a narrower grant.

Examples:
  python3 scripts/dev-jwt/make_token.py
  python3 scripts/dev-jwt/make_token.py --scope cashflow.entries.write --subject pos-integration
  python3 scripts/dev-jwt/make_token.py --scope cashflow.consolidation.read --subject reporting-integration

  # Negative test: a token with the wrong audience -> 401.
  python3 scripts/dev-jwt/make_token.py --audience not-cashflow-api
"""
import argparse
import base64
import hashlib
import hmac
import json
import time

DEFAULT_KEY = "cashflow-local-dev-signing-key-32-bytes-minimum-change-me"
DEFAULT_ISSUER = "cashflow-dev-issuer"
DEFAULT_AUDIENCE = "cashflow-api"
DEFAULT_SCOPES = ["cashflow.entries.write", "cashflow.entries.read", "cashflow.consolidation.read"]


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--scope", action="append", default=[],
        help="Scope, e.g. cashflow.entries.write (repeatable; default: the three scopes)")
    parser.add_argument("--subject", default="dev-user", help="sub claim (principal id)")
    parser.add_argument("--ttl-minutes", type=int, default=60, help="Token lifetime in minutes")
    parser.add_argument("--issuer", default=DEFAULT_ISSUER)
    parser.add_argument(
        "--audience", default=DEFAULT_AUDIENCE,
        help="Override the aud claim (e.g. to mint a deliberately wrong one for a negative test)")
    parser.add_argument("--key", default=DEFAULT_KEY, help="Must match Jwt:SigningKey on the service(s) validating this token")
    args = parser.parse_args()

    scopes = args.scope if args.scope else DEFAULT_SCOPES

    now = int(time.time())
    header = {"alg": "HS256", "typ": "JWT"}
    payload = {
        "iss": args.issuer,
        "aud": args.audience,
        "sub": args.subject,
        "iat": now,
        "nbf": now,
        "exp": now + args.ttl_minutes * 60,
        "scope": " ".join(scopes),
    }

    signing_input = f"{b64url(json.dumps(header, separators=(',', ':')).encode())}.{b64url(json.dumps(payload, separators=(',', ':')).encode())}"
    signature = hmac.new(args.key.encode("utf-8"), signing_input.encode("ascii"), hashlib.sha256).digest()
    token = f"{signing_input}.{b64url(signature)}"

    print(token)


if __name__ == "__main__":
    main()
