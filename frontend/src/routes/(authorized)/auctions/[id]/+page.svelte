<script lang="ts">
	import Imageset from '$lib/components/common/imageset.svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
</script>

<svelte:head>
	<title>Auction</title>
</svelte:head>

<div class="flex flex-col gap-10">
	<h1 class="h1">{data.auction.title}</h1>
	<Imageset links={data.auction.imageUrls}></Imageset>
	<div>
		{data.auction.description}
	</div>
</div>
