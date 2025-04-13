<script lang="ts">
	import { goto } from '$app/navigation';

	let was_wrong: boolean = false;

	let admin_key: string = '';

	async function attempt_login() {
		was_wrong = false;
		const res = await fetch('/api/admin/login', {
			body: admin_key,
			method: 'POST'
		});
		if (res.ok) {
			goto('/admin');
		}
		was_wrong = true;
	}
</script>

<div class="container mx-auto">
	<h1 class="h1 text-center">Login</h1>

	<div class="card p-10">
		<label class="label">
			<span>Admin Key</span>
			<input
				type="password"
				class="input {was_wrong ? 'input-error' : ''}"
				bind:value={admin_key}
			/>
		</label>
		<button type="button" class="btn" on:click={attempt_login}>Login</button>
	</div>
</div>
