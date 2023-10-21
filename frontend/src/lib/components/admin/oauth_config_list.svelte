<script lang="ts">
	import OAuthProviderComponent from './oauth_provider.svelte';

	import { AdminApi } from '$lib/api';
	import { AppConfig, OAuthProvider } from '$lib/types/oauth_providers';

	export let app_config: AppConfig;
	export let oauth_providers: OAuthProvider[];

	function add_provider() {
		oauth_providers.push(new OAuthProvider());
		oauth_providers = oauth_providers;
	}

	function save_provider(provider: OAuthProvider) {
		new AdminApi().createProvider(provider);
	}

	async function deleteProvider(provider: OAuthProvider): Promise<void> {
		await new AdminApi().deleteProvider(provider.name);
		const idx = oauth_providers.indexOf(provider);
		oauth_providers.splice(idx, 1);
		oauth_providers = oauth_providers;
	}
</script>

<h3 class="h3">Oauth config</h3>
<div>
	{#each oauth_providers as provider}
		<div class="card p-2">
			<OAuthProviderComponent bind:provider {app_config} />
			<button
				type="button"
				class="btn variant-filled-primary"
				on:click={() => save_provider(provider)}>Save</button
			>
			<button
				type="button"
				class="btn variant-filled-warning"
				on:click={() => deleteProvider(provider)}>Delete</button
			>
		</div>
	{/each}
</div>
<hr class="!border-t-4" />
<button type="button" class="btn variant-filled" on:click={add_provider}>Add oauth2 provider</button
>
