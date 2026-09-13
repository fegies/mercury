# Playwright end-to-end test for Project Mercury.
#
# This script runs INSIDE the NixOS test VM (see integration.nix) via
# `mercury.succeed("mercury-e2e")`. It drives a real headless Chromium and walks
# through the complete user journey:
#   1. log in through the mock OIDC provider (the browser follows the whole
#      signin redirect chain on its own)
#   2. create an auction as the admin, with a near-term closing time
#   3. place a bid on the freshly created auction
#   4. let the closing time pass and observe the auction auto-close (the
#      backend's expiry machinery appends an expired close; the browser then
#      shows the auction as Closed)
#
# A non-zero exit code makes the NixOS test fail.

import re
import sys
import time
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
            # The closing time is editable at second precision (the form's
            # auction-end input has step="1"), so set a near-term deadline
            # that gives the create and bid steps a few seconds of headroom
            # while keeping the auto-close wait short.
            closes_at = datetime.now() + timedelta(seconds=20)
            closes_at_utc = closes_at.astimezone(timezone.utc)

            page.goto(BASE + "/manage-auctions/new", wait_until="domcontentloaded")
            page.locator('input[name="name"]').fill("Test Auction")
            page.locator('textarea[name="description"]').fill(
                "Created by the integration test"
            )
            page.locator('input[name="min-price"]').fill("5")
            page.locator('input[name="auction-end"]').fill(
                closes_at.strftime("%Y-%m-%dT%H:%M:%S")
            )
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

            # --- 3. Place a bid --------------------------------
            page.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            page.locator('input[name="maximum_amount"]').fill("42")
            page.get_by_role("button", name="Place bid").click()

            # Bid result is rendered after the form action returns.
            expect(page.get_by_text("Bid placed.")).to_be_visible(timeout=30000)
            expect(page.get_by_text("You are the highest bidder")).to_be_visible()
            expect(page.get_by_text("€42.00")).to_be_visible()
            # The auction is still open; the close is only ~15s away.
            expect(page.get_by_role("heading", name="Place a bid")).to_be_visible()

            # --- 4. Auto-close on expiry -----------------------
            # Wait until a little past the closing time, then poll for the
            # Closed state. The backend's expired-close path needs a small
            # amount of wall-clock time to fire after the deadline passes.
            remaining = (closes_at_utc - datetime.now(timezone.utc)).total_seconds()
            time.sleep(max(remaining, 5) + 5)

            reload_until = time.monotonic() + 25
            closed_seen = False
            while time.monotonic() < reload_until:
                page.reload(wait_until="domcontentloaded")
                if page.get_by_text("Closed", exact=True).count() > 0:
                    closed_seen = True
                    break
                time.sleep(2)

            if not closed_seen:
                print("AUTO-CLOSE-FAILURE url=", page.url)
                print("AUTO-CLOSE-FAILURE title=", page.title())
                print(page.content())
                msg = "auction did not auto-close after its closing time"
                raise AssertionError(msg)

            # The public auction page replaced the bid form with the Closed badge.
            expect(page.get_by_text("Closed", exact=True)).to_be_visible()
            expect(page.get_by_role("heading", name="Place a bid")).not_to_be_visible()

            # A closed auction is still listed on the public auctions page.
            page.goto(BASE + "/auctions", wait_until="domcontentloaded")
            expect(page.get_by_text("Test Auction")).to_be_visible(timeout=30000)

            print(f"SUCCESS auction_id={auction_id}")
            return 0
        finally:
            browser.close()


if __name__ == "__main__":
    sys.exit(main())
