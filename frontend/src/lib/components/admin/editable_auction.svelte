<script lang="ts">
	import type { AuctionSummary } from '$lib/types/auction';
	import { FileUpload } from '@skeletonlabs/skeleton-svelte';

	import IconDropzone from '@lucide/svelte/icons/image-plus';
	import IconFile from '@lucide/svelte/icons/paperclip';
	import IconRemove from '@lucide/svelte/icons/circle-x';

	let {
		model = $bindable(),
		submit_label = 'Create Auction',
		upload_label = 'Images',
		action = '?'
	}: {
		model: AuctionSummary;
		submit_label?: string;
		upload_label?: string;
		action?: string;
	} = $props();
</script>

<form method="POST" {action} class="flex flex-col gap-4" enctype="multipart/form-data">
	<label class="label">
		<span class="label-text">Name</span>
		<input class="input" type="text" name="name" value={model.title} />
	</label>

	<label class="label">
		<span class="label-text">{upload_label}</span>
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
			value={model.minimumPrice}
		/>
	</label>

	<label class="label">
		<span class="label-text">Auction end (local time)</span>
		<input
			type="datetime-local"
			class="input"
			name="auction-end"
			min={new Date().toISOString().slice(0, 16)}
			value={new Date(model.closureTime).toISOString().slice(0, 16)}
		/>
	</label>

	<input type="submit" class="btn preset-filled-primary-500 w-fit" value={submit_label} />
</form>
