<script lang="ts">
	import type { AuctionSummary } from '$lib/types/auction';

	let {
		model = $bindable(),
		submit_label = 'Create Auction',
		action = '?',
		min_closure = null,
		extend_only = false
	}: {
		model: AuctionSummary;
		submit_label?: string;
		action?: string;
		min_closure?: string | null;
		extend_only?: boolean;
	} = $props();
</script>

<form method="POST" {action} class="flex flex-col gap-4" enctype="multipart/form-data">
	<label class="label">
		<span class="label-text">Name</span>
		<input class="input" type="text" name="name" value={model.title} />
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
			step="1"
			class="input"
			name="auction-end"
			min={new Date(min_closure ?? Date.now()).toISOString().slice(0, 16)}
			value={new Date(model.closureTime).toISOString().slice(0, 16)}
		/>
		{#if extend_only}
			<span class="text-surface-500 text-sm">
				The auction end can only be extended; it cannot be moved closer.
			</span>
		{/if}
	</label>

	<label class="flex items-center gap-2">
		<input type="checkbox" class="checkbox" name="published" checked={model.isPublished} />
		<span class="label-text">Published (visible to non-admin users)</span>
	</label>

	<input type="submit" class="btn preset-filled-primary-500 w-fit" value={submit_label} />
</form>
