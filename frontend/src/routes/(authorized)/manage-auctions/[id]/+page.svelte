<script lang="ts">
	import EditableAuction from '$lib/components/admin/editable_auction.svelte';
	import { FileUpload } from '@skeletonlabs/skeleton-svelte';
	import type { ActionData, PageData } from './$types';

	import IconDropzone from '@lucide/svelte/icons/image-plus';
	import IconFile from '@lucide/svelte/icons/paperclip';
	import IconRemove from '@lucide/svelte/icons/circle-x';

	let { data, form }: { data: PageData; form: ActionData } = $props();

	let auction = $derived(form?.auction ?? data.auction);
</script>

{#if form?.success}
	<div class="card preset-filled-success-500 mb-5 p-4">Saved.</div>
{/if}

{#if form?.errors && form.errors.length > 0}
	<div class="card preset-tonal-surface mb-5 flex flex-col gap-2 p-4">
		<span>There were errors: </span>
		{#each form.errors as error (error)}
			<span class="card preset-tonal-error p-4">{error}</span>
		{/each}
	</div>
{/if}

<h1 class="h1 mb-5">Edit “{data.auction.title}”</h1>

<section class="mb-8">
	<h2 class="h3 mb-3">Details</h2>
	<EditableAuction bind:model={auction} submit_label="Save Changes" action="?/update"
	></EditableAuction>
</section>

<section class="mb-8 flex flex-col gap-3">
	<h2 class="h3">Images</h2>

	{#if data.auction.imageUrls.length > 0}
		<div class="flex flex-wrap gap-3">
			{#each data.auction.imageUrls as url (url)}
				{@const imageId = url.split('/').pop()}
				<div class="relative">
					<img src={url} alt="" class="bg-surface-800 rounded-container size-32 object-contain" />
					<form method="POST" action="?/remove_image">
						<input type="hidden" name="image_id" value={imageId} />
						<button type="submit" class="btn preset-filled-error-500 absolute top-1 right-1">
							<IconRemove class="size-4" />
						</button>
					</form>
				</div>
			{/each}
		</div>
	{:else}
		<span>This auction has no images yet.</span>
	{/if}

	<form
		method="POST"
		action="?/add_images"
		enctype="multipart/form-data"
		class="flex flex-col gap-4"
	>
		<label class="label">
			<span class="label-text">Upload images</span>
			<FileUpload name="images" accept="image/*" classes="w-full" maxFiles={20}>
				{#snippet iconInterface()}<IconDropzone class="size-8" />{/snippet}
				{#snippet iconFile()}<IconFile class="size-4" />{/snippet}
				{#snippet iconFileRemove()}<IconRemove class="size-4" />{/snippet}
			</FileUpload>
		</label>
		<input type="submit" class="btn preset-filled-primary-500 w-fit" value="Add Images" />
	</form>
</section>

<section class="flex flex-col gap-3">
	<h2 class="h3">Danger zone</h2>
	{#if data.auction.isClosed}
		<span class="preset-filled-error-500 badge">Closed</span>
	{:else}
		<p>Closing an auction is permanent. Bids can no longer be placed afterwards.</p>
		<form method="POST" action="?/close">
			<button type="submit" class="btn preset-filled-error-500 w-fit">Close Auction</button>
		</form>
	{/if}
</section>
