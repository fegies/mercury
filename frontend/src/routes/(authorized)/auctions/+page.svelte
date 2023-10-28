<script lang="ts">
	type Auction = {
		id: string;
		item_name: string;
		picture_url: string;
		current_price: number;
		currency: string;
	};

	let auctions: Auction[] = [
		{
			id: 'ateruanern',
			item_name: 'Laptop',
			picture_url: '',
			current_price: 15,
			currency: '€'
		},
		{
			id: 'faeu',
			item_name: 'Screen',
			picture_url: '',
			current_price: 20,
			currency: '€'
		}
	];
</script>

<h1 class="h1">Auctions</h1>

<div class="flex flex-col gap-5">
	{#each auctions as auction}
		<a href="/auctions/{auction.id}">
			<div class="card p-5 flex gap-10">
				<div class="">
					<div class="placeholder h-64 w-64" />
				</div>
				<div class="my-5">
					<a href="/auctions/{auction.id}" class="h3">{auction.item_name}</a>
					<br />
					<span>{auction.current_price}{auction.currency}</span>
				</div>
			</div>
		</a>
	{/each}
</div>
