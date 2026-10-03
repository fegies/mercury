<script lang="ts">
	import IconCamera from '@lucide/svelte/icons/camera';

	let { on_capture }: { on_capture: (file: File) => void } = $props();

	function handle_change(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		const file = input.files?.[0];
		if (file) {
			on_capture(file);
		}
		input.value = '';
	}
</script>

<label class="btn preset-tonal cursor-pointer">
	<IconCamera class="size-4" />
	Take photo
	<input
		type="file"
		accept="image/*"
		capture="environment"
		class="hidden"
		onchange={handle_change}
	/>
</label>
