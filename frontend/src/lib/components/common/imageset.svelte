<script lang="ts">
	let {
		links
	}: {
		links: string[];
	} = $props();

	let selected = $derived(links[0]);
</script>

{#if links.length > 0}
	<div
		class="bg-surface-900 rounded-container grid h-[50vh] w-full grid-flow-row grid-rows-5 items-center gap-2"
	>
		<div class="row-span-4 flex h-full items-center">
			<img
				src={selected}
				class="rounded-container bg-surface-800 h-full w-full object-contain"
				alt="item"
			/>
			<!-- <div class="placeholder h-full w-full"></div> -->
		</div>
		<div class="row-span-1 flex h-full max-w-full flex-nowrap gap-4 overflow-x-scroll">
			{#each links as link}
				<button class="aspect-square h-auto w-56" onclick={() => (selected = link)}>
					<img
						src={link}
						class="bg-surface-800 rounded-container border-primary-800 h-full w-full object-contain {selected ===
						link
							? 'border-4'
							: ''}"
						alt="item"
					/>
				</button>
			{/each}
		</div>
	</div>
{/if}
