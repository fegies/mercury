<script lang="ts">
	import Imageset from '$lib/components/common/imageset.svelte';
	import { build_browser_client } from '$lib/api';
	import { LIVE_STREAM_CONTEXT_KEY, type LiveStream } from '$lib/live';
	import { getContext } from 'svelte';
	import type { AuctionSummary } from '$lib/types/auction.js';
	import type { ActionData, PageData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	const stream = getContext<LiveStream>(LIVE_STREAM_CONTEXT_KEY);

	let live_auction = $state<AuctionSummary | null>(null);
	let auction = $derived(live_auction ?? data.auction);

	let currentBid = $derived(
		live_auction?.currentBid ?? form?.bid?.currentBid ?? auction.currentBid ?? auction.minimumPrice
	);
	let myHighest = $derived(
		live_auction?.myHighest ?? form?.bid?.myHighest ?? auction.myHighest ?? 0
	);
	let i_am_highest_bidder = $derived(
		live_auction?.isHighestBidder ?? form?.bid?.isHighestBidder ?? auction.isHighestBidder
	);

	let min_increase = $derived(Math.max(myHighest, currentBid) + 0.5);

	let i_placed_a_bet = $derived(myHighest > 0);

	$effect(() => {
		const id = data.auction.id;
		live_auction = null;
		const off = stream.on_auction_update(id, () => {
			void refetch_auction(id);
		});
		return () => {
			off();
		};
	});

	async function refetch_auction(id: string) {
		const client = build_browser_client();
		const { data: summary, error: apiError } = await client.getApiAuctionsById({ path: { id } });
		if (!apiError && summary) {
			live_auction = summary;
		}
	}
</script>

<svelte:head>
	<title>Auction</title>
</svelte:head>

<div class="flex flex-col gap-10">
	<h1 class="h1">{auction.title}</h1>
	<Imageset links={auction.imageUrls}></Imageset>
	<div>
		{auction.description}
	</div>

	{#if auction.isClosed}
		<span class="preset-filled-error-500 badge w-fit">Closed</span>
	{:else}
		<section class="card flex flex-col gap-4 p-6">
			<h2 class="h3">Place a bid</h2>

			<p>
				Current price: <strong>€{currentBid.toFixed(2)}</strong>
				{#if i_am_highest_bidder}
					<span
						class="inline-flex items-center rounded-md bg-green-400/10 px-2 py-1 align-middle text-xs font-medium text-green-400 inset-ring inset-ring-green-500/20"
						>You are the highest bidder</span
					>
				{:else if i_placed_a_bet}
					<span
						class="inline-flex items-center rounded-md bg-red-400/10 px-2 py-1 text-xs font-medium text-red-400 inset-ring inset-ring-red-400/20"
						>You have been outbid</span
					>
				{/if}
			</p>

			{#if myHighest != null}
				<p>
					Your maximum bid: <strong>€{myHighest.toFixed(2)}</strong>
				</p>
			{/if}

			{#if form?.bid}
				<div class="card preset-filled-success-500 p-4">Bid placed.</div>
			{/if}

			{#if form?.errors && form.errors.length > 0}
				<div class="card preset-tonal-surface flex flex-col gap-2 p-4">
					<span>There were errors: </span>
					{#each form.errors as error (error)}
						<span class="card preset-tonal-error p-4">{error}</span>
					{/each}
				</div>
			{/if}

			<form method="POST" action="?/bid" class="flex flex-col gap-4">
				<label class="label">
					<span class="label-text">Raise your maximum bid</span>
					<input
						type="number"
						name="maximum_amount"
						step="0.5"
						min={currentBid + 0.5}
						required
						class="input"
						value={min_increase}
					/>
				</label>
				<input type="submit" class="btn preset-filled-primary-500 w-fit" value="Place bid" />
			</form>
		</section>
	{/if}
</div>
