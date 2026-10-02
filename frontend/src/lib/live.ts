import { browser } from '$app/environment';
import { build_browser_client } from '$lib/api';
import type { LiveEvent } from '$lib/client/types.gen';

export type LiveNotification = LiveEvent & { kind: 'Notification' };
export type LiveAuctionUpdate = LiveEvent & { kind: 'AuctionUpdated' };

export type NotificationListener = (event: LiveNotification) => void;
export type AuctionUpdateListener = (event: LiveAuctionUpdate) => void;

let started = false;
let stream_abort: AbortController | null = null;
const notification_listeners = new Set<NotificationListener>();
const any_auction_listeners = new Set<AuctionUpdateListener>();
const per_auction_listeners = new Map<string, Set<AuctionUpdateListener>>();

export function on_notification(listener: NotificationListener): () => void {
	notification_listeners.add(listener);
	return () => notification_listeners.delete(listener);
}

export function on_any_auction_update(listener: AuctionUpdateListener): () => void {
	any_auction_listeners.add(listener);
	return () => any_auction_listeners.delete(listener);
}

export function on_auction_update(auctionId: string, listener: AuctionUpdateListener): () => void {
	let listeners = per_auction_listeners.get(auctionId);
	if (!listeners) {
		listeners = new Set();
		per_auction_listeners.set(auctionId, listeners);
	}
	listeners.add(listener);
	return () => {
		listeners.delete(listener);
		if (listeners.size === 0) {
			per_auction_listeners.delete(auctionId);
		}
	};
}

export function handle_live_event(event: LiveEvent): void {
	if (event.kind === 'Notification') {
		const notification = event as LiveNotification;
		for (const listener of notification_listeners) listener(notification);
		return;
	}

	const update = event as LiveAuctionUpdate;
	for (const listener of any_auction_listeners) listener(update);
	per_auction_listeners.get(event.auctionId)?.forEach((listener) => listener(update));
}

export function start_live_stream(): () => void {
	if (!browser || started) {
		return stop_live_stream;
	}
	started = true;
	stream_abort = new AbortController();
	void drain_live_events(stream_abort.signal);
	return stop_live_stream;
}

function stop_live_stream(): void {
	if (!started) {
		return;
	}
	started = false;
	stream_abort?.abort();
	stream_abort = null;
}

async function drain_live_events(signal: AbortSignal): Promise<void> {
	const client = build_browser_client();
	const { stream } = await client.getApiEventsStream({ credentials: 'same-origin', signal });
	// The generated ServerSentEventsResult maps the stream element type through
	// `TData[keyof TData]` in this hey-api release, mangling object payloads; the
	// runtime yields parsed LiveEvent objects, hence the documented cast. The
	// stream itself reconnects with exponential backoff and ends once the
	// signal above is aborted.
	for await (const event of stream as AsyncGenerator<LiveEvent, void, unknown>) {
		handle_live_event(event);
	}
}
