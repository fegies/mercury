<script lang="ts">
	import DateCountdownBadge from '$lib/components/common/DateCountdownBadge.svelte';
	import type { AuctionSummary } from '$lib/types/auction.js';

	import IconImage from '@lucide/svelte/icons/image-off';

	let {
		auction
	}: {
		auction: AuctionSummary;
	} = $props();

	let current_bid = $derived(auction.currentBid ?? auction.minimumPrice);
</script>

<div class="flex h-64 w-full justify-center sm:w-64">
	{#if auction.imageUrls.length > 0}
		<img
			src={auction.imageUrls[0]}
			alt="Auction item"
			class="bg-surface-800 rounded-container h-full w-full object-contain"
		/>
	{:else}
		<IconImage class="bg-surface-800 rounded-container h-full w-full p-10"></IconImage>
	{/if}
</div>
<div class="flex flex-col gap-5">
	<span class="h3">{auction.title} </span>
	<p>
		Current price: <strong>€{current_bid.toFixed(2)}</strong>
		{#if auction.isHighestBidder}
			<span
				class="inline-flex items-center rounded-md bg-green-400/10 px-2 py-1 align-middle text-xs font-medium text-green-400 inset-ring inset-ring-green-500/20"
				>You are the highest bidder</span
			>
		{:else if (auction.myHighest ?? 0) > 0}
			<span
				class="inline-flex items-center rounded-md bg-red-400/10 px-2 py-1 text-xs font-medium text-red-400 inset-ring inset-ring-red-400/20"
				>You have been outbid</span
			>
		{/if}
	</p>
	<span
		>Ends {new Date(auction.closureTime).toLocaleString()} (<DateCountdownBadge
			expiryDate={new Date(auction.closureTime)}
		></DateCountdownBadge>)</span
	>
</div>
