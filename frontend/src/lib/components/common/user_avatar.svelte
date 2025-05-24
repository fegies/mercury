<script lang="ts">
	import { Avatar } from '@skeletonlabs/skeleton-svelte';
	import type { Snippet } from 'svelte';

	let {
		user,
		menu
	}: {
		user: User;
		menu: Snippet;
	} = $props();

	let menu_open = $state(false);
</script>

<div>
	<button
		onclick={() => {
			menu_open = !menu_open;
		}}
	>
		<Avatar src="/api/users/picture/{user.id}" name={user.name} />
	</button>

	{#if menu_open}
		<div class="card bg-secondary-800 fixed right-2 mt-2 p-5">
			{@render menu()}
		</div>
	{/if}
</div>
