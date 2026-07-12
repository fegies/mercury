<script lang="ts">
	import type { UserInfo } from '$lib/client/types.gen';
	import { Avatar } from '@skeletonlabs/skeleton-svelte';
	import type { Snippet } from 'svelte';

	let {
		user,
		menu
	}: {
		user: UserInfo;
		menu: Snippet;
	} = $props();

	let menu_open = $state(false);

	function toggleMenu() {
		menu_open = !menu_open;
	}
</script>

<div>
	<button onclick={toggleMenu}>
		<Avatar src={user.profilePictureUrl || undefined} name={user.name} />
	</button>

	{#if menu_open}
		<!-- svelte-ignore a11y_click_events_have_key_events -->
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 z-[900] h-screen w-screen" onclick={toggleMenu}></div>
		<div class="card bg-secondary-800 fixed right-2 z-[950] mt-2 p-5">
			{@render menu()}
		</div>
	{/if}
</div>
