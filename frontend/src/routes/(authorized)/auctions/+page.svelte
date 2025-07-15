<script lang="ts">
	import AuctionOverview from './AuctionOverview.svelte';

	import DateCountdownBadge from '$lib/components/common/DateCountdownBadge.svelte';
	import type { PageData } from './$types';

	import IconImage from '@lucide/svelte/icons/image-off';

	let { data }: { data: PageData } = $props();
</script>

<h1 class="h1">Auctions</h1>

<div class="flex flex-col gap-5">
	{#each data.auctions as auction}
		<a href="/auctions/{auction.id}">
			<div class="card flex gap-10 p-5">
				<AuctionOverview {auction}></AuctionOverview>
			</div>
		</a>
	{/each}
</div>
