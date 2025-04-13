<script lang="ts">
	import { Avatar } from '@skeletonlabs/skeleton-svelte';

	let {
		user
	}: {
		user: User;
	} = $props();
</script>

<Avatar src="/api/users/picture/{user.id}" name={user.name} />
