# Playwright end-to-end test for Project Mercury.
#
# This script runs INSIDE the NixOS test VM (see integration.nix) via
# `mercury.succeed("mercury-e2e")`. It drives real headless Chromium contexts
# for an admin, two bidders and a mobile-emulated bidder and walks through
# the complete user journey:
#   1. log in through the mock OIDC provider (the browser follows the whole
#      signin redirect chain on its own)
#   2. create an auction as the admin, with a near-term closing time
#   3. place bids and verify the live notification chain over the SSE stream
#      (through the nginx proxy): the outbid bidder gets a toast plus live
#      price and badge updates, the new leader gets none, the winner gets a
#      won toast when the auction auto-closes, and bidders get a toast when
#      an admin cancels an auction they bid on
#   4. a mobile actor (Playwright's iPhone 13 device profile: phone viewport,
#      mobile user agent, touch events) performs the same login and bidding
#      journey on the second auction, driven by taps instead of clicks
#   5. key pages are rendered at a 360px-wide viewport and must not overflow
#      horizontally, the most common mobile layout defect
#
# Screenshots of every flow (desktop and mobile) are written to
# /tmp/screenshots inside the VM; integration.nix copies the directory into
# the derivation output, where they land as result/screenshots/<name>.png.
#
# All waiting is done through Playwright's expectation API (which polls the
# DOM); the script never sleeps.
#
# A non-zero exit code makes the NixOS test fail.

import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

from playwright.sync_api import sync_playwright, expect

BASE = "http://localhost"
SCREENSHOTS = Path("/tmp/screenshots")

# Playwright's device profile for the mobile actor: 390x844 viewport, touch
# enabled, mobile user agent, device scale factor 3.
MOBILE_DEVICE = "iPhone 13"

# The responsive checks use an even narrower width than the mobile actor's
# device profile so that small Android phones are covered as well.
NARROW_VIEWPORT = {"width": 360, "height": 800}
DESKTOP_VIEWPORT = {"width": 1280, "height": 720}


def shot(page, name, full_page=False):
    page.screenshot(
        path=str(SCREENSHOTS / f"{name}.png"),
        full_page=full_page,
        animations="disabled",
    )


def login(browser, who, context_kwargs=None, authorize_screenshot=None):
    context = browser.new_context(**(context_kwargs or {}))
    page = context.new_page()
    page.goto(BASE + "/auctions", wait_until="domcontentloaded")
    authorize = page.get_by_role("button", name=f"Authorize as {who}", exact=True)
    try:
        authorize.wait_for(timeout=45000)
    except Exception:
        print(f"LOGIN-FAILURE({who}) url={page.url!r}")
        print("LOGIN-FAILURE title=", page.title())
        print(page.content())
        raise
    if authorize_screenshot:
        shot(page, authorize_screenshot)
    authorize.click()

    if who == "admin":
        # The "Manage Auctions" link is only rendered for admins, so its
        # presence proves both login and the Admin role.
        page.get_by_role("link", name="Manage Auctions").wait_for(timeout=45000)
    else:
        page.get_by_role("heading", name="Auctions").wait_for(timeout=45000)
    return page


def create_auction(admin, title, closes_at, screenshot=None):
    admin.goto(BASE + "/manage-auctions/new", wait_until="domcontentloaded")
    admin.locator('input[name="name"]').fill(title)
    admin.locator('textarea[name="description"]').fill("Created by the integration test")
    admin.locator('input[name="min-price"]').fill("5")
    admin.locator('input[name="auction-end"]').fill(closes_at.strftime("%Y-%m-%dT%H:%M:%S"))
    admin.locator('input[name="published"]').check()
    if screenshot:
        shot(admin, screenshot)
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


def place_bid_by_touch(page, amount):
    page.locator('input[name="maximum_amount"]').tap()
    page.locator('input[name="maximum_amount"]').fill(amount)
    page.get_by_role("button", name="Place bid").tap()
    expect(page.get_by_text("Bid placed.")).to_be_visible(timeout=30000)


def assert_no_horizontal_overflow(page, url, wait_for):
    page.goto(BASE + url, wait_until="domcontentloaded")
    expect(wait_for(page)).to_be_visible()
    overflow = page.evaluate(
        """() => {
            const de = document.documentElement;
            const body = document.body;
            return Math.max(
                de.scrollWidth - de.clientWidth,
                body.scrollWidth - body.clientWidth,
            );
        }"""
    )
    if overflow > 1:
        shot(page, f"overflow-{url.strip('/').replace('/', '-')}")
        raise AssertionError(f"{url} overflows horizontally by {overflow}px at 360px width")


def main() -> int:
    SCREENSHOTS.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(
            args=["--no-sandbox", "--headless", "--disable-gpu"],
            channel="chromium",
        )
        try:
            admin = login(browser, "admin", authorize_screenshot="desktop-login-authorize")
            bidder = login(browser, "bidder")
            bidder2 = login(browser, "bidder2")
            mobile = login(
                browser,
                "mobile",
                context_kwargs=p.devices[MOBILE_DEVICE],
                authorize_screenshot="mobile-login-authorize",
            )

            # The closing time is editable at second precision (the form's
            # auction-end input has step="1"). The deadline must outlast the
            # notification steps below (which take a few seconds once the
            # SSE stream is live) and still arrive quickly within the test.
            closes_at = datetime.now() + timedelta(seconds=30)
            closes_at_utc = closes_at.astimezone(timezone.utc)
            auction_id = create_auction(
                admin, "Test Auction", closes_at, screenshot="desktop-new-auction-form"
            )

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
            shot(admin, "desktop-auction-detail-before-outbid")
            bidder.goto(BASE + "/auctions", wait_until="domcontentloaded")
            row = bidder.locator(f'a[href="/auctions/{auction_id}"]')
            expect(row.get_by_text("€5.00")).to_be_visible()

            # --- Outbid: bidder2 takes the lead ---------------------------
            bidder2.goto(BASE + f"/auctions/{auction_id}", wait_until="domcontentloaded")
            place_bid(bidder2, "60")

            # With second-max + increment pricing the new price is €50.50.
            # Price assertions are scoped to the row/card so the toast
            # description (which also names the price) cannot collide.
            expect(bidder.get_by_text("Outbid on Test Auction")).to_be_visible(timeout=10000)
            expect(row.get_by_text("€50.50")).to_be_visible(timeout=10000)
            expect(row.get_by_text("You have been outbid")).to_be_visible()
            shot(bidder, "desktop-auctions-list-outbid", full_page=True)
            expect(admin.get_by_text("€50.50")).to_be_visible(timeout=10000)
            shot(admin, "desktop-auction-detail-after-outbid")

            # The leader must not receive an outbid toast.
            expect(bidder2.get_by_text("Outbid on Test Auction")).not_to_be_visible()

            # --- Auto-close: the winner sees it live -----------------------
            # The toast and the Closed badge arrive over the SSE stream; the
            # timeout covers the remaining wall clock until the deadline plus
            # the expiry worker's latency.
            remaining = (closes_at_utc - datetime.now(timezone.utc)).total_seconds()
            close_timeout = int(max(remaining, 0) * 1000 + 20000)

            expect(bidder2.get_by_text("You won Test Auction")).to_be_visible(timeout=close_timeout)
            expect(bidder2.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)
            shot(bidder2, "desktop-auction-won-closed")
            expect(admin.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)

            # A closed auction is still listed on the public auctions page.
            admin.goto(BASE + "/auctions", wait_until="domcontentloaded")
            expect(admin.get_by_text("Test Auction")).to_be_visible(timeout=30000)

            # --- Cancellation: bidders are notified ------------------------
            far_close = datetime.now() + timedelta(hours=1)
            second_id = create_auction(admin, "Second Auction", far_close)

            bidder.goto(BASE + f"/auctions/{second_id}", wait_until="domcontentloaded")
            place_bid(bidder, "20")

            # The mobile actor performs the same journey with touch input at
            # a phone viewport: list -> detail -> bid, all driven by taps.
            mobile.goto(BASE + "/auctions", wait_until="domcontentloaded")
            mobile_row = mobile.locator(f'a[href="/auctions/{second_id}"]')
            expect(mobile_row).to_be_visible()
            shot(mobile, "mobile-auctions-list", full_page=True)
            mobile_row.tap()
            expect(mobile.get_by_role("heading", name="Second Auction")).to_be_visible()
            shot(mobile, "mobile-auction-detail")
            place_bid_by_touch(mobile, "25")
            expect(mobile.get_by_text("You are the highest bidder")).to_be_visible()
            shot(mobile, "mobile-bid-placed")

            admin.goto(BASE + f"/manage-auctions/{second_id}", wait_until="domcontentloaded")
            admin.get_by_role("button", name="Cancel Auction").click()

            expect(bidder.get_by_text("Second Auction was cancelled")).to_be_visible(timeout=10000)
            expect(bidder.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)
            shot(bidder, "desktop-auction-cancelled")

            # The cancellation reaches the mobile viewport over SSE too, and
            # the closed state replaces the bid form there as well.
            expect(mobile.get_by_text("Second Auction was cancelled")).to_be_visible(timeout=10000)
            expect(mobile.get_by_text("Closed", exact=True).first).to_be_visible(timeout=10000)
            shot(mobile, "mobile-auction-cancelled")

            # --- Responsive checks -----------------------------------------
            # At a small phone viewport none of the key pages may scroll
            # horizontally. This uses the admin context (desktop user agent),
            # whose viewport is restored afterwards.
            admin.set_viewport_size(NARROW_VIEWPORT)
            overflow_checks = [
                ("/auctions", lambda p: p.get_by_role("heading", name="Auctions")),
                (
                    f"/auctions/{auction_id}",
                    lambda p: p.get_by_role("heading", name="Test Auction"),
                ),
                ("/manage-auctions", lambda p: p.get_by_role("heading", name="Manage Auctions")),
                (
                    "/manage-auctions/new",
                    lambda p: p.get_by_role("button", name="Create Auction"),
                ),
            ]
            for url, wait_for in overflow_checks:
                assert_no_horizontal_overflow(admin, url, wait_for)
            admin.set_viewport_size(DESKTOP_VIEWPORT)

            print(f"SUCCESS auction_id={auction_id} second_id={second_id}")
            return 0
        finally:
            browser.close()


if __name__ == "__main__":
    sys.exit(main())
