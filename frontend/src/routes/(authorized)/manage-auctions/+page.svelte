<script lang="ts">
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<div class="mb-5 flex items-center justify-between">
	<h1 class="h1">Manage Auctions</h1>
	<a class="btn preset-filled-primary-500" href="/manage-auctions/new">Create Auction</a>
</div>

{#if data.auctions.length === 0}
	<span>No auctions yet.</span>
{:else}
	<div class="flex flex-col gap-5">
		{#each data.auctions as auction (auction.id)}
			<div class="card flex items-center gap-5 p-5">
				<span class="h4 flex-1 truncate">{auction.title}</span>
				<span>{auction.currentBid ?? auction.minimumPrice}€</span>
				{#if auction.isClosed}
					<span class="badge preset-filled-error-500">Closed</span>
				{/if}
				<a class="btn preset-tonal" href="/auctions/{auction.id}">View</a>
				<a class="btn preset-filled-primary-500" href="/manage-auctions/{auction.id}">Edit</a>
			</div>
		{/each}
	</div>
{/if}
