<script lang="ts">
	import { AdminApi } from '$lib/api';
	import { OAuthProvider } from '$lib/types/oauth_providers';
	let oauth_providers: OAuthProvider[];

	function add_provider() {
		oauth_providers.push(new OAuthProvider());
		oauth_providers = oauth_providers;
	}

	function formValidate(value: string | null, validator?: (value: string) => boolean) {
		const is_valid = value && (!validator || validator(value));
		return is_valid ? '' : 'input-warning';
	}
	function is_url(value: string): boolean {
		try {
			const url = new URL(value);
			return url.protocol === 'https:';
		} catch {
			return false;
		}
	}
	function save_provider(provider: OAuthProvider) {
		AdminApi.createProvider(provider);
	}

	async function deleteProvider(provider: OAuthProvider): Promise<void> {
		await AdminApi.deleteProvider(provider.id);
		const idx = oauth_providers.indexOf(provider);
		oauth_providers.splice(idx, 1);
		oauth_providers = oauth_providers;
	}

	async function loadProviders() {
		oauth_providers = await AdminApi.loadOauthProviders();
	}
</script>

{#await loadProviders() then _}
	<h3 class="h3">Oauth config</h3>
	<div>
		{#each oauth_providers as provider}
			<div class="card p-2">
				<label class="label">
					<span>Name</span>
					<input
						class="input {formValidate(provider.name)}"
						type="text"
						placeholder="name"
						bind:value={provider.name}
					/>
				</label>
				<label class="label">
					<span>Auth Url</span>
					<input
						class="input {formValidate(provider.auth_url, is_url)}"
						type="text"
						placeholder="Auth url"
						bind:value={provider.auth_url}
					/>
				</label>
				<label class="label">
					<span>Redirect URL</span>
					<input
						type="text"
						class="input {formValidate(provider.redirect_url, is_url)}"
						placeholder="Redirect URL"
						bind:value={provider.redirect_url}
					/>
				</label>
				<label>
					<span>Token URL</span>
					<input
						type="text"
						class="input {formValidate(provider.token_url, is_url)}"
						placeholder="Token URL"
						bind:value={provider.token_url}
					/>
				</label>
				<label>
					<span>Client Id</span>
					<input
						type="text"
						class="input {formValidate(provider.client_id)}"
						placeholder="client id"
						bind:value={provider.client_id}
					/>
				</label>
				<label>
					<span>Client Secret</span>
					<input
						type="password"
						class="input {formValidate(provider.client_secret)}"
						placeholder="client secret"
						bind:value={provider.client_secret}
					/>
				</label>
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
	<button type="button" class="btn variant-filled" on:click={add_provider}
		>Add oauth2 provider</button
	>
{/await}
