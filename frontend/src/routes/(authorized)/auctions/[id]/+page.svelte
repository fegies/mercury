<script lang="ts">
	import type { PageData } from './$types';

	export let data: PageData;
</script>

<svelte:head>
	<title>Auction</title>
</svelte:head>

<h1 class="h1">Auction {data.test}</h1>
