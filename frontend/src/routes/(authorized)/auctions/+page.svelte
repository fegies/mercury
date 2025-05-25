<script lang="ts">
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
	console.log(data);
</script>

<h1 class="h1">Auctions</h1>

<div class="flex flex-col gap-5">
	{#each data.auctions as auction}
		<a href="/auctions/{auction.id}">
			<div class="card flex gap-10 p-5">
				<div class="flex h-64 w-64 justify-center">
					{#if auction.first_image}
						<img src={auction.first_image} alt="Auction item" class="max-h-full max-w-full" />
					{:else}
						<div class="placeholder h-full w-full"></div>
					{/if}
				</div>
				<div class="my-5">
					<span class="h3">{auction.item_name}</span>
					<br />
					<span>{auction.current_value}€</span>
				</div>
			</div>
		</a>
	{/each}
</div>
