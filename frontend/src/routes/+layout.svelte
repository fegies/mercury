<script lang="ts">
	import '../app.css';

	let { children, data } = $props();
</script>

<svelte:head>
	<title>{data.branding}</title>
</svelte:head>

{@render children()}
