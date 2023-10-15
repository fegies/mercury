<script lang="ts">
	import { AdminApi } from '$lib/api';
	import type { AppConfig, OAuthProvider } from '$lib/types/oauth_providers';

	export let app_config: AppConfig;
	export let provider: OAuthProvider;

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

	$: {
		provider.redirect_url = `${app_config.deployment_domain}/oauth/flows/${provider.name}/callback`;
	}
</script>

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
		class="input"
		readonly={true}
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
