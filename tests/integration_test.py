# Playwright end-to-end test for Project Mercury.
#
# This script runs INSIDE the NixOS test VM (see integration.nix) via
# `mercury.succeed("mercury-e2e")`. It drives a real headless Chromium and walks
# through the complete user journey:
#   1. log in through the mock OIDC provider (the browser follows the whole
#      signin redirect chain on its own)
#   2. create an auction as the admin
#   3. place a bid on the freshly created auction
#
# A non-zero exit code makes the NixOS test fail.

import re
import sys
from datetime import datetime, timedelta, timezone

from playwright.sync_api import sync_playwright, expect

BASE = "http://localhost"


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch(
            args=["--no-sandbox", "--headless", "--disable-gpu"],
            channel="chromium",
        )
        page = browser.new_page()
        try:
            # --- 1. OIDC login --------------------------------
            # Server-side redirect chain: /auctions -> /api/login
            # -> IdP authorize form -> /signin-oidc -> back to
            # /auctions. The real browser follows every hop and
            # lands on the mock IdP's consent page, where we pick
            # the admin identity.
            page.goto(BASE + "/auctions", wait_until="domcontentloaded")
            admin_btn = page.get_by_role("button", name="Authorize as admin")
            try:
                admin_btn.wait_for(timeout=45000)
            except Exception:
                print(f"LOGIN-FAILURE url={page.url!r}")
                print("LOGIN-FAILURE title=", page.title())
                print(page.content())
                raise
            admin_btn.click()

            # Authenticated: back on the auctions list. The
            # "Manage Auctions" link is only rendered for admins,
            # so its presence proves both login and the Admin role.
            manage_link = page.get_by_role("link", name="Manage Auctions")
            manage_link.wait_for(timeout=45000)
            expect(page.get_by_role("heading", name="Auctions")).to_be_visible()

            # --- 2. Create an auction --------------------------
            page.goto(BASE + "/manage-auctions/new", wait_until="domcontentloaded")
            page.locator('input[name="name"]').fill("Test Auction")
            page.locator('textarea[name="description"]').fill(
                "Created by the integration test"
            )
            page.locator('input[name="min-price"]').fill("5")
            future = (
                datetime.now(timezone.utc) + timedelta(hours=2)
            ).strftime("%Y-%m-%dT%H:%M")
            page.locator('input[name="auction-end"]').fill(future)
            page.locator('input[name="published"]').check()
            page.get_by_role("button", name="Create Auction").click()

            # Submitting redirects (303) to /manage-auctions/{id};
            # grab the id from the URL.
            page.wait_for_load_state("domcontentloaded")
            match = re.search(r"/manage-auctions/([^/]+)", page.url)
            if not match:
                msg = f"could not find auction id in URL {page.url!r}"
                raise AssertionError(msg)
            auction_id = match.group(1)

            # The new auction card appears on the public list.
            page.goto(BASE + "/auctions", wait_until="domcontentloaded")
            expect(page.get_by_text("Test Auction")).to_be_visible(timeout=30000)

            # --- 3. Place a bid --------------------------------
            page.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            page.locator('input[name="maximum_amount"]').fill("42")
            page.get_by_role("button", name="Place bid").click()

            # Bid result is rendered after the form action returns.
            expect(page.get_by_text("Bid placed.")).to_be_visible(timeout=30000)
            expect(page.get_by_text("You are the highest bidder")).to_be_visible()
            expect(page.get_by_text("€42.00")).to_be_visible()

            print(f"SUCCESS auction_id={auction_id}")
            return 0
        finally:
            browser.close()


if __name__ == "__main__":
    sys.exit(main())
