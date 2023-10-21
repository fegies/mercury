<script lang="ts">
	import { UserApi } from '$lib/api';
	import { SlideToggle } from '@skeletonlabs/skeleton';
	import UserAvatar from '../common/user_avatar.svelte';

	export let user: User;

	function updateUser(user: User): void {
		new UserApi().updateUser(user);
	}
</script>

<div class="grid grid-cols-3 mx-10">
	<div class="flex items-center">
		<UserAvatar {user} />
		<span class="p-5">{user.name}</span>
	</div>
	<div class="flex items-center">
		<SlideToggle
			name="can_start_auctions"
			bind:checked={user.can_start_auctions}
			on:change={() => updateUser(user)}>Can start auctions</SlideToggle
		>
	</div>
</div>
