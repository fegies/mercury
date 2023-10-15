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
						<a class="button" href={provider.flow_url}>{provider.provider_name}</a>
					</span>
				</li>
			{/each}
		</ul>
	{/await}
</div>
