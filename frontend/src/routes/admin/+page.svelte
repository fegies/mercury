<script lang="ts">
	import { goto } from '$app/navigation';
	import { AdminApi, UserApi } from '$lib/api';
	import OauthConfigList from '$lib/components/admin/oauth_config_list.svelte';
	import UserConfiguration from '$lib/components/admin/user_configuration.svelte';
	import type { AppConfig } from '$lib/types/oauth_providers';
	import { Autocomplete, SlideToggle } from '@skeletonlabs/skeleton';

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

	{#await loadConfig() then _}
		<div class="p-10">
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
		</div>
		<div class="p-10">
			<OauthConfigList app_config={config} />
		</div>
	{/await}

	<div class="p-10">
		{#await new UserApi().list_users() then users}
			<h3 class="h3">User Config</h3>
			<div class="card p-2">
				{#each users as user}
					<UserConfiguration {user} />
				{/each}
			</div>
		{/await}
	</div>
</div>
