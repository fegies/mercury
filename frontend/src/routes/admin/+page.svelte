<script lang="ts">
	import { goto } from '$app/navigation';
	import { AdminApi } from '$lib/api';
	import OauthConfig from '$lib/components/admin/oauth_config.svelte';
	import type { AppConfig } from '$lib/types/oauth_providers';

	let config: AppConfig;
	async function loadConfig(): Promise<AppConfig> {
		config = await AdminApi.loadConfig();
		return config;
	}

	if (typeof document == 'object' && document.cookie.indexOf('MERCURY_IS_ADMIN') < 0)
		goto('/admin/login');

	async function saveConfig() {
		await AdminApi.saveConfig(config);
	}
</script>

<div class="container mx-auto my-10">
	<h1 class="h1 text-center">Configuration</h1>

	<div class="p-10">
		{#await loadConfig() then _}
			<h3>General Config</h3>
			<div class="card p-2">
				<label class="label">
					<span>Deployment Domain</span>
					<input
						class="input"
						type="text"
						placeholder="Deployment Domain"
						bind:value={config.deployment_domain}
					/>
				</label>
				<button type="submit" class="btn variant-filled" on:click={saveConfig}>Save</button>
			</div>
		{/await}
	</div>

	<div class="p-10">
		<OauthConfig />
	</div>
</div>
