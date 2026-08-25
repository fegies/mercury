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
