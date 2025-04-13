<script lang="ts">
	import UserAvatar from '$lib/components/common/user_avatar.svelte';
	import type { LayoutProps } from './$types';
	import { AppBar } from '@skeletonlabs/skeleton-svelte';

	let { children, data }: LayoutProps = $props();
</script>

<!-- App Bar -->
<AppBar>
	{#snippet lead()}
		<div class="flex items-center gap-10">
			<a href="/" class="card p-5">
				<strong class="text-xl uppercase">Mercury</strong>
			</a>
			<a href="/auctions" class="card p-5">Auctions</a>
			{#if data.me.can_start_auctions}
				<a href="/manage-auctions" class="card p-5">Manage Auctions</a>
			{/if}
		</div>
	{/snippet}

	{#snippet trail()}
		<div>
			<UserAvatar user={data?.me} />
		</div>
	{/snippet}
</AppBar>

<div class="container mx-auto mt-10">
	{@render children()}
</div>
