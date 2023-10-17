<script lang="ts">
	import { AppBar, AppShell, Avatar } from '@skeletonlabs/skeleton';
	import type { LayoutData } from '../$types';

	export let data: LayoutData;
	$: initials = (data?.me?.name ?? '')
		.split(/\s/)
		.map((w: string) => w[0])
		.join('')
		.substring(0, 2);
</script>

<AppShell>
	<svelte:fragment slot="header">
		<!-- App Bar -->
		<AppBar>
			<svelte:fragment slot="lead">
				<strong class="text-xl uppercase">Mercury</strong>
			</svelte:fragment>

			<svelte:fragment slot="trail">
				<Avatar src="/api/users/picture/{data.me.id}" {initials} />
			</svelte:fragment>
		</AppBar>
	</svelte:fragment>
	<!-- Page Route Content -->
	<slot />
</AppShell>
