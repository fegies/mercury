<script lang="ts">
	import { onMount } from 'svelte';

	let {
		expiryDate
	}: {
		expiryDate: Date;
	} = $props();

	let now = $state(new Date());

	let remainingTime = $derived.by(() => {
		const total_secs = ((expiryDate.getTime() - now.getTime()) / 1000) | 0;

		const full_mins = (total_secs / 60) | 0;
		const full_hours = (full_mins / 60) | 0;
		const full_days = (full_hours / 24) | 0;
		const weeks = (full_days / 7) | 0;

		return {
			expired: total_secs <= 0,
			weeks,
			days: full_days % 7,
			hours: full_hours % 24,
			minutes: full_mins % 60,
			seconds: total_secs % 60
		};
	});

	let longText = $derived.by(() => {
		if (remainingTime.expired) return 'EXPIRED';
		return JSON.stringify(remainingTime);
	});

	let shortText = $derived.by(() => {
		if (remainingTime.expired) return 'EXPIRED';

		if (remainingTime.weeks > 1) return `${remainingTime.weeks} weeks`;

		return longText;
	});

	onMount(() => {
		const interval = setInterval(() => {
			now = new Date();
		}, 1000);

		return () => clearInterval(interval);
	});
</script>

<span title={longText}>{shortText}</span>
