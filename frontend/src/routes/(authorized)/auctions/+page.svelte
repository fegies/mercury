<script lang="ts">
	import AuctionOverview from './AuctionOverview.svelte';
	import { build_browser_client } from '$lib/api';
	import { use_live_stream } from '$lib/live';
	import { onMount } from 'svelte';
	import type { AuctionSummary } from '$lib/types/auction.js';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const stream = use_live_stream();

	// Refreshed summaries patch the SSR-loaded list per auction: a ping
	// refetches only the auction that changed, and the overlay is keyed so
	// rows update without touching the rest of the list.
	let refreshed = $state(new Map<string, AuctionSummary>());
	let auctions = $derived(data.auctions.map((auction) => refreshed.get(auction.id) ?? auction));

	onMount(() => {
		return stream.on_any_auction_update((event) => {
			if (data.auctions.some((auction) => auction.id === event.auctionId)) {
				void refetch_auction(event.auctionId);
			}
		});
	});

	async function refetch_auction(id: string) {
		const client = build_browser_client();
		const { data: summary, error: apiError } = await client.getApiAuctionsById({ path: { id } });
		if (!apiError && summary) {
			refreshed.set(id, summary);
			refreshed = new Map(refreshed);
		}
	}
</script>

<h1 class="h1">Auctions</h1>

<div class="flex flex-col gap-5">
	{#each auctions as auction (auction.id)}
		<a href="/auctions/{auction.id}">
			<div class="card flex flex-col gap-5 p-5 sm:flex-row sm:gap-10">
				<AuctionOverview {auction}></AuctionOverview>
			</div>
		</a>
	{/each}
</div>
