<script lang="ts">
	import { goto } from '$app/navigation';
	import { AdminApi, UserApi } from '$lib/api';
	import OauthConfigList from '$lib/components/admin/oauth_config_list.svelte';
	import UserConfiguration from '$lib/components/admin/user_configuration.svelte';

	export let data;

	if (typeof document == 'object' && document.cookie.indexOf('MERCURY_IS_ADMIN') < 0)
		goto('/admin/login');

	async function saveConfig() {
		await new AdminApi().saveConfig(data.config);
	}
</script>

<div class="container mx-auto mt-10">
	<h1 class="h1 text-center">Configuration</h1>

	<div class="p-10">
		<h3>General Config</h3>
		<div class="card p-2">
			<label class="label">
				<span>Deployment Domain</span>
				<input
					class="input"
					type="text"
					placeholder="Deployment Domain"
					bind:value={data.config.deployment_domain}
				/>
			</label>
			<button type="submit" class="btn variant-filled" on:click={saveConfig}>Save</button>
		</div>
	</div>
	<div class="p-10">
		<!-- <OauthConfigList app_config={data.config} oauth_providers={data.oauth_providers} /> -->
	</div>

	<div class="p-10">
		<h3 class="h3">User Config</h3>
		<div class="card p-2">
			{#each data.users as user}
				<UserConfiguration {user} />
			{/each}
		</div>
	</div>
</div>
