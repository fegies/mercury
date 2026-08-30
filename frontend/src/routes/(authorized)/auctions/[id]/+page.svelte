<script lang="ts">
	import Imageset from '$lib/components/common/imageset.svelte';
	import type { ActionData, PageData } from './$types';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let currentBid = $derived(form?.bid?.currentBid ?? data.auction.currentBid ?? null);
	let myHighest = $derived(form?.bid?.myHighest ?? data.auction.myHighest ?? null);
	let isHighestBidder = $derived(form?.bid?.isHighestBidder ?? data.auction.isHighestBidder);
</script>

<svelte:head>
	<title>Auction</title>
</svelte:head>

<div class="flex flex-col gap-10">
	<h1 class="h1">{data.auction.title}</h1>
	<Imageset links={data.auction.imageUrls}></Imageset>
	<div>
		{data.auction.description}
	</div>

	{#if data.auction.isClosed}
		<span class="preset-filled-error-500 badge w-fit">Closed</span>
	{:else}
		<section class="card flex flex-col gap-4 p-6">
			<h2 class="h3">Place a bid</h2>

			{#if currentBid !== null}
				<p>
					Current price: <strong>€{currentBid.toFixed(2)}</strong>
					{#if isHighestBidder}
						<span class="preset-filled-success-500 badge">You are the highest bidder</span>
					{/if}
				</p>
			{/if}

			{#if myHighest !== null}
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
					<span class="label-text">Your maximum bid</span>
					<input
						type="number"
						name="maximum_amount"
						step="0.01"
						min="0"
						required
						class="input"
						placeholder={currentBid !== null ? (currentBid + 0.5).toFixed(2) : '0.00'}
					/>
				</label>
				<input type="submit" class="btn preset-filled-primary-500 w-fit" value="Place bid" />
			</form>
		</section>
	{/if}
</div>
