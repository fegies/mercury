<script lang="ts">
	import UserAvatar from '$lib/components/common/user_avatar.svelte';
	import { on_notification, start_live_stream } from '$lib/live';
	import type { LayoutProps } from './$types';
	import { AppBar, createToaster, Toaster } from '@skeletonlabs/skeleton-svelte';
	import { onMount } from 'svelte';

	let { children, data }: LayoutProps = $props();

	const toaster = createToaster({ placement: 'bottom-end' });

	onMount(() => {
		const stop_stream = start_live_stream();
		const off_notification = on_notification((event) => {
			const title = event.title ?? 'an auction';
			const price = event.price != null ? `€${event.price.toFixed(2)}` : null;
			switch (event.type) {
				case 'Outbid':
					toaster.warning({
						title: `Outbid on ${title}`,
						description: price ? `The current price is now ${price}` : undefined
					});
					break;
				case 'Won':
					toaster.success({
						title: `You won ${title}`,
						description: price ? `Winning price: ${price}` : undefined
					});
					break;
				case 'Cancelled':
					toaster.error({
						title: `${title} was cancelled`,
						description: 'The auction was cancelled by an admin.'
					});
					break;
			}
		});
		return () => {
			stop_stream();
			off_notification();
		};
	});
</script>

<!-- App Bar -->
<AppBar>
	{#snippet lead()}
		<div class="flex items-center gap-10">
			<a href="/" class="card p-5">
				<strong class="text-xl uppercase">{data.branding}</strong>
			</a>
			<a href="/auctions" class="card p-5">Auctions</a>
			{#if data.me}
				<a href="/manage-auctions" class="card p-5">Manage Auctions</a>
			{/if}
		</div>
	{/snippet}

	{#snippet trail()}
		<UserAvatar user={data?.me?.userinfo}>
			{#snippet menu()}
				<a href="/api/logout">Logout</a>
			{/snippet}
		</UserAvatar>
	{/snippet}
</AppBar>

<div class="container mx-auto mt-10">
	{@render children()}
</div>

<Toaster {toaster}></Toaster>
