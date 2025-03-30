<script lang="ts">
	import { AppBar, AppShell } from '@skeletonlabs/skeleton';
	import UserAvatar from '$lib/components/common/user_avatar.svelte';

	export let data: { me: User };
</script>

<AppShell>
	<svelte:fragment slot="header">
		<!-- App Bar -->
		<AppBar>
			<svelte:fragment slot="lead">
				<div class="flex items-center gap-10">
					<a href="/" class="card p-5">
						<strong class="text-xl uppercase">Mercury</strong>
					</a>
					<a href="/auctions" class="card p-5">Auctions</a>
					{#if data.me.can_start_auctions}
						<a href="/manage-auctions" class="card p-5">Manage Auctions</a>
					{/if}
				</div>
			</svelte:fragment>

			<svelte:fragment slot="trail">
				<div>
					<UserAvatar user={data?.me} />
				</div>
			</svelte:fragment>
		</AppBar>
	</svelte:fragment>

	<div class="container mx-auto mt-10">
		<slot />
	</div>
</AppShell>
