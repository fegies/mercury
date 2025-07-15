<script lang="ts">
	import Imageset from '$lib/components/common/imageset.svelte';
	import type { PageData } from './$types';

	export let data: PageData;
</script>

<svelte:head>
	<title>Auction</title>
</svelte:head>

<div class="flex flex-col gap-10">
	<h1 class="h1">{data.auction.item_name}</h1>
	<Imageset links={data.image_links}></Imageset>
	<div>
		{data.auction.description}
	</div>
</div>
