<script lang="ts">
	import DateCountdownBadge from '$lib/components/common/DateCountdownBadge.svelte';
	import type { AuctionSummary } from '$lib/types/auction.js';

	import IconImage from '@lucide/svelte/icons/image-off';

	let {
		auction
	}: {
		auction: AuctionSummary;
	} = $props();

	let current_bid = $state(auction.currentBid);
</script>

<div class="flex h-64 w-64 justify-center">
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
<div class="my-5 flex flex-col">
	<span class="h3">{auction.title}</span>
	<span>{current_bid ?? auction.minimumPrice}€</span>
	<span
		>Ends {new Date(auction.closureTime).toLocaleString()} (<DateCountdownBadge
			expiryDate={new Date(auction.closureTime)}
		></DateCountdownBadge>)</span
	>
</div>
