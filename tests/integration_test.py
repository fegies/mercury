# Playwright end-to-end test for Project Mercury.
#
# This script runs INSIDE the NixOS test VM (see integration.nix) via
# `mercury.succeed("mercury-e2e")`. It drives real headless Chromium contexts
# for an admin and two bidders and walks through the complete user journey:
#   1. log in through the mock OIDC provider (the browser follows the whole
#      signin redirect chain on its own)
#   2. create an auction as the admin, with a near-term closing time
#   3. place bids and verify the live notification chain over the SSE stream
#      (through the nginx proxy): the outbid bidder gets a toast plus live
#      price and badge updates, the new leader gets none, the winner gets a
#      won toast when the auction auto-closes, and bidders get a toast when
#      an admin cancels an auction they bid on
#
# All waiting is done through Playwright's expectation API (which polls the
# DOM); the script never sleeps.
#
# A non-zero exit code makes the NixOS test fail.

import re
import sys
from datetime import datetime, timedelta, timezone

from playwright.sync_api import sync_playwright, expect

BASE = "http://localhost"


def login(browser, who):
    context = browser.new_context()
    page = context.new_page()
    page.goto(BASE + "/auctions", wait_until="domcontentloaded")
    authorize = page.get_by_role("button", name=f"Authorize as {who}")
    try:
        authorize.wait_for(timeout=45000)
    except Exception:
        print(f"LOGIN-FAILURE({who}) url={page.url!r}")
        print("LOGIN-FAILURE title=", page.title())
        print(page.content())
        raise
    authorize.click()

    if who == "admin":
        # The "Manage Auctions" link is only rendered for admins, so its
        # presence proves both login and the Admin role.
        page.get_by_role("link", name="Manage Auctions").wait_for(timeout=45000)
    else:
        page.get_by_role("heading", name="Auctions").wait_for(timeout=45000)
    return page


def create_auction(admin, title, closes_at):
    admin.goto(BASE + "/manage-auctions/new", wait_until="domcontentloaded")
    admin.locator('input[name="name"]').fill(title)
    admin.locator('textarea[name="description"]').fill("Created by the integration test")
    admin.locator('input[name="min-price"]').fill("5")
    admin.locator('input[name="auction-end"]').fill(closes_at.strftime("%Y-%m-%dT%H:%M:%S"))
    admin.locator('input[name="published"]').check()
    admin.get_by_role("button", name="Create Auction").click()

    admin.wait_for_load_state("domcontentloaded")
    match = re.search(r"/manage-auctions/([^/]+)", admin.url)
    if not match:
        msg = f"could not find auction id in URL {admin.url!r}"
        raise AssertionError(msg)
    return match.group(1)


def place_bid(page, amount):
    page.locator('input[name="maximum_amount"]').fill(amount)
    page.get_by_role("button", name="Place bid").click()
    expect(page.get_by_text("Bid placed.")).to_be_visible(timeout=30000)


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch(
            args=["--no-sandbox", "--headless", "--disable-gpu"],
            channel="chromium",
        )
        try:
            admin = login(browser, "admin")
            bidder = login(browser, "bidder")
            bidder2 = login(browser, "bidder2")

            # The closing time is editable at second precision (the form's
            # auction-end input has step="1"). The deadline must outlast the
            # notification steps below and still arrive within the test.
            closes_at = datetime.now() + timedelta(seconds=75)
            closes_at_utc = closes_at.astimezone(timezone.utc)
            auction_id = create_auction(admin, "Test Auction", closes_at)

            # --- Bidder takes the lead ------------------------------------
            bidder.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            place_bid(bidder, "50")
            expect(bidder.get_by_text("You are the highest bidder")).to_be_visible()
            expect(bidder.get_by_text("€50.00")).to_be_visible()

            # --- Observers take their positions ---------------------------
            # Admin watches the detail page, bidder watches the list; both
            # must see the upcoming bid live, without reloading.
            admin.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            expect(admin.get_by_text("€5.00")).to_be_visible()
            bidder.goto(BASE + "/auctions", wait_until="domcontentloaded")
            expect(bidder.get_by_text("€5.00")).to_be_visible()

            # --- Outbid: bidder2 takes the lead ---------------------------
            bidder2.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            place_bid(bidder2, "60")

            # With second-max + increment pricing the new price is €50.50.
            expect(bidder.get_by_text("Outbid on Test Auction")).to_be_visible(timeout=10000)
            expect(bidder.get_by_text("€50.50")).to_be_visible(timeout=10000)
            expect(bidder.get_by_text("You have been outbid")).to_be_visible()
            expect(admin.get_by_text("€50.50")).to_be_visible(timeout=10000)

            # The leader must not receive an outbid toast.
            expect(bidder2.get_by_text("Outbid on Test Auction")).not_to_be_visible()

            # --- Auto-close: the winner sees it live -----------------------
            # The toast and the Closed badge arrive over the SSE stream; the
            # timeout covers the remaining wall clock until the deadline plus
            # the expiry worker's latency.
            remaining = (closes_at_utc - datetime.now(timezone.utc)).total_seconds()
            close_timeout = int(max(remaining, 0) + 20000)

            expect(bidder2.get_by_text("You won Test Auction")).to_be_visible(timeout=close_timeout)
            expect(bidder2.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)
            expect(admin.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)

            # A closed auction is still listed on the public auctions page.
            admin.goto(BASE + "/auctions", wait_until="domcontentloaded")
            expect(admin.get_by_text("Test Auction")).to_be_visible(timeout=30000)

            # --- Cancellation: bidders are notified ------------------------
            far_close = datetime.now() + timedelta(hours=1)
            second_id = create_auction(admin, "Second Auction", far_close)

            bidder.goto(BASE + f"/auctions/{second_id}", wait_until="domcontentloaded")
            place_bid(bidder, "20")

            admin.goto(BASE + f"/manage-auctions/{second_id}", wait_until="domcontentloaded")
            admin.get_by_role("button", name="Cancel Auction").click()

            expect(bidder.get_by_text("Second Auction was cancelled")).to_be_visible(timeout=10000)
            expect(bidder.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)

            print(f"SUCCESS auction_id={auction_id} second_id={second_id}")
            return 0
        finally:
            browser.close()


if __name__ == "__main__":
    sys.exit(main())
