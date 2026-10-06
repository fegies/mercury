<script lang="ts">
	import UserAvatar from '$lib/components/common/user_avatar.svelte';
	import { LiveStream, provide_live_stream } from '$lib/live';
	import type { LayoutProps } from './$types';
	import { AppBar, createToaster, Toaster } from '@skeletonlabs/skeleton-svelte';
	import { onDestroy } from 'svelte';

	let { children, data }: LayoutProps = $props();

	const toaster = createToaster({ placement: 'bottom-end' });

	const stream = new LiveStream();
	stream.start();
	provide_live_stream(stream);

	const off_notification = stream.on_notification((event) => {
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

	onDestroy(() => {
		off_notification();
		stream.stop();
	});
</script>

<!-- App Bar -->
<AppBar>
	{#snippet lead()}
		<div class="flex flex-wrap items-center gap-3 sm:gap-10">
			<a href="/" class="card p-3 sm:p-5">
				<strong class="text-xl uppercase">{data.branding}</strong>
			</a>
			<a href="/auctions" class="card p-3 sm:p-5">Auctions</a>
			{#if data.me}
				<a href="/manage-auctions" class="card p-3 sm:p-5">Manage Auctions</a>
			{/if}
		</div>
	{/snippet}

	{#snippet trail()}
		<UserAvatar user={data?.me?.userinfo}>
			{#snippet menu()}
				<form method="post" action="/api/logout">
					<button type="submit" class="block w-full cursor-pointer text-left hover:underline">
						Logout
					</button>
				</form>
				<form method="post" action="/api/logout?idp=1">
					<button type="submit" class="block w-full cursor-pointer text-left hover:underline">
						Sign out of SSO
					</button>
				</form>
			{/snippet}
		</UserAvatar>
	{/snippet}
</AppBar>

<div class="container mx-auto mt-10">
	{@render children()}
</div>

<Toaster {toaster}></Toaster>
