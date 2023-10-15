<script lang="ts">
	import { AppBar, AppShell } from '@skeletonlabs/skeleton';
	import type { LayoutData } from '../$types';

	export let data: LayoutData;
</script>

<AppShell>
	<svelte:fragment slot="header">
		<!-- App Bar -->
		<AppBar>
			<svelte:fragment slot="lead">
				<strong class="text-xl uppercase">Mercury</strong>
			</svelte:fragment>

			<svelte:fragment slot="trail">
				{data.me.name}
			</svelte:fragment>
		</AppBar>
	</svelte:fragment>
	<!-- Page Route Content -->
	<slot />
</AppShell>
