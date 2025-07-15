<script lang="ts">
	import DateCountdownBadge from '$lib/components/common/DateCountdownBadge.svelte';

	import IconImage from '@lucide/svelte/icons/image-off';
	import { onMount } from 'svelte';

	let {
		auction
	}: {
		auction: {
			first_image: string | null;
			item_name: string;
			current_value: number;
			end_time: Date;
			id: string;
		};
	} = $props();

	let current_value = $state(auction.current_value);

	onMount(async () => {
		const resp = await fetch(`/auctions/${auction.id}/liveticker`);
		if (resp.body) {
			const reader = resp.body.pipeThrough(new TextDecoderStream()).getReader();
			while (true) {
				const { done, value } = await reader.read();
				console.log(done, value);
				if (done) break;
			}
		}
	});
</script>

<div class="flex h-64 w-64 justify-center">
	{#if auction.first_image}
		<img
			src={auction.first_image}
			alt="Auction item"
			class="bg-surface-800 rounded-container h-full w-full object-contain"
		/>
	{:else}
		<IconImage class="bg-surface-800 rounded-container h-full w-full p-10"></IconImage>
	{/if}
</div>
<div class="my-5 flex flex-col">
	<span class="h3">{auction.item_name}</span>
	<span>{current_value}€</span>
	<span
		>Ends {auction.end_time.toLocaleString()} (<DateCountdownBadge expiryDate={auction.end_time}
		></DateCountdownBadge>)</span
	>
</div>
