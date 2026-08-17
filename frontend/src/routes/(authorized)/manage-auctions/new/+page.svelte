<script lang="ts">
	import EditableAuction from '$lib/components/admin/editable_auction.svelte';
	import { default_auction, type AuctionSummary } from '$lib/types/auction';
	import type { ActionData } from './$types';

	let { form }: { form: ActionData } = $props();

	let auction = $derived(form?.auction || default_auction());
</script>

{#if form?.errors && form.errors.length > 0}
	<div class="card preset-tonal-surface mb-5 flex flex-col gap-2 p-4">
		<span>There were errors: </span>
		{#each form.errors as error}
			<span class="card preset-tonal-error p-4">{error}</span>
		{/each}
	</div>
{/if}

<EditableAuction bind:model={auction}></EditableAuction>
