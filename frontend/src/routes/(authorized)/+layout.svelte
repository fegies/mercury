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
				<strong class="text-xl uppercase">Mercury</strong>
			</svelte:fragment>

			<svelte:fragment slot="trail">
				<UserAvatar user={data?.me} />
			</svelte:fragment>
		</AppBar>
	</svelte:fragment>
	<!-- Page Route Content -->
	<slot />
</AppShell>
