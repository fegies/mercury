<script lang="ts">
	import type { PageProps } from './$types';

	let props: PageProps = $props();

	const providers = props.data.providers;
</script>

<div class="container mx-auto my-10">
	<h1 class="h1">Choose your provider</h1>
	<ul class="list my-10">
		{#each providers as provider}
			<li>
				<span class="flex-auto">
					<a class="btn preset-filled btn-lg" href={provider.auth_url}>{provider.name}</a>
				</span>
			</li>
		{:else}
			No Oauth provider configured so far. Please visit the <a href="/admin">Admin page</a> to configure
			some.
		{/each}
	</ul>
</div>
