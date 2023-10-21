<script lang="ts">
	import { Avatar } from '@skeletonlabs/skeleton';

	export let user: User;
	let initials = user.name
		.split(/\s/)
		.map((w: string) => w[0])
		.join('')
		.substring(0, 2);
</script>

<Avatar src="/api/users/picture/{user.id}" {initials} />
