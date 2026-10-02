<script lang="ts">
	import AuctionOverview from './AuctionOverview.svelte';
	import { build_browser_client } from '$lib/api';
	import { LIVE_STREAM_CONTEXT_KEY, type LiveStream } from '$lib/live';
	import { getContext } from 'svelte';
	import type { AuctionSummary } from '$lib/types/auction.js';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();

	const stream = getContext<LiveStream>(LIVE_STREAM_CONTEXT_KEY);

	let live_auctions = $state<AuctionSummary[] | null>(null);
	let auctions = $derived(live_auctions ?? data.auctions);

	$effect(() => {
		live_auctions = null;
		const off = stream.on_any_auction_update(() => {
			void refetch_auctions();
		});
		return () => {
			off();
		};
	});

	async function refetch_auctions() {
		const client = build_browser_client();
		const { data: summaries, error: apiError } = await client.getApiAuctions();
		if (!apiError && summaries) {
			live_auctions = summaries;
		}
	}
</script>

<h1 class="h1">Auctions</h1>

<div class="flex flex-col gap-5">
	{#each auctions as auction (auction.id)}
		<a href="/auctions/{auction.id}">
			<div class="card flex gap-10 p-5">
				<AuctionOverview {auction}></AuctionOverview>
			</div>
		</a>
	{/each}
</div>
