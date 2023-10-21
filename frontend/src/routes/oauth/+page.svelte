<script lang="ts">
	import { OAuthApi } from '$lib/api';

	type Provider = {
		name: string;
		flow_url: string;
	};
</script>

<div class="container mx-auto my-10">
	<h1 class="h1">Choose your provider</h1>
	{#await OAuthApi.listProviders() then providers}
		<ul class="list my-10">
			{#each providers as provider}
				<li>
					<span class="flex-auto">
						<a class="btn variant-ghost-surface" href={provider.flow_url}
							>{provider.provider_name}</a
						>
					</span>
				</li>
			{:else}
				No Oauth provider configured so far. Please visit the <a href="/admin">Admin page</a> to configure
				some.
			{/each}
		</ul>
	{/await}
</div>
