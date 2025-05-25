<script lang="ts">
	import type { DetailedAuction } from '$lib/types/auction';
	import { FileUpload } from '@skeletonlabs/skeleton-svelte';

	import IconDropzone from '@lucide/svelte/icons/image-plus';
	import IconFile from '@lucide/svelte/icons/paperclip';
	import IconRemove from '@lucide/svelte/icons/circle-x';

	let {
		model = $bindable()
	}: {
		model: DetailedAuction;
	} = $props();
</script>

<form method="POST" class="flex flex-col gap-4" enctype="multipart/form-data">
	<label class="label">
		<span class="label-text">Name</span>
		<input class="input" type="text" name="name" value={model.name} />
	</label>

	<label class="label">
		<span class="label-text">Images</span>
		<FileUpload name="images" accept="image/*" classes="w-full" maxFiles={20}>
			{#snippet iconInterface()}<IconDropzone class="size-8" />{/snippet}
			{#snippet iconFile()}<IconFile class="size-4" />{/snippet}
			{#snippet iconFileRemove()}<IconRemove class="size-4" />{/snippet}
		</FileUpload>
	</label>

	<label class="label">
		<span class="label-text">Description</span>
		<textarea class="textarea" name="description" value={model.description}></textarea>
	</label>

	<label class="label">
		<span class="label-text">Minimum price (Euro)</span>
		<input
			type="number"
			step="0.5"
			class="input"
			name="min-price"
			min="0"
			value={model.min_price}
		/>
	</label>

	<label class="label">
		<span class="label-text">Auction end (local time)</span>
		<input
			type="datetime-local"
			class="input"
			name="auction-end"
			min={new Date().toLocaleString()}
			value={model.auction_end.toLocaleString()}
		/>
	</label>

	<input type="submit" class="input" value="Create Auction" />
</form>
