import { browser } from '$app/environment';
import { getContext, setContext } from 'svelte';
import { build_browser_client } from '$lib/api';
import type { LiveEvent } from '$lib/client/types.gen';

export type LiveNotification = LiveEvent & { kind: 'Notification' };
export type LiveAuctionUpdate = LiveEvent & { kind: 'AuctionUpdated' };

export type NotificationListener = (event: LiveNotification) => void;
export type AuctionUpdateListener = (event: LiveAuctionUpdate) => void;

const live_stream_key = Symbol('mercury.live-stream');

/**
 * What a subpage may do with the shared stream: register listeners. The
 * lifecycle (start/stop) stays with the component that opened it.
 */
export type LiveStreamListener = Pick<
	LiveStream,
	'on_notification' | 'on_any_auction_update' | 'on_auction_update'
>;

/**
 * Called by the authorized layout during component initialization to share
 * its stream with subpages.
 */
export function provide_live_stream(stream: LiveStream): void {
	setContext(live_stream_key, stream);
}

/**
 * Called by a subpage during component initialization to register listeners
 * against the stream the authorized layout provided. Throws when absent, so
 * a missing provider fails loudly instead of silently doing nothing.
 */
export function use_live_stream(): LiveStreamListener {
	const stream = getContext<LiveStream | null>(live_stream_key);
	if (!stream) {
		throw new Error(
			'use_live_stream() must run under the authorized layout that provides the stream'
		);
	}
	return stream;
}

/**
 * One live SSE connection with its own event routing. Whoever opens an
 * instance keeps it in a local variable, starts it when mounted, and stops
 * it on unmount; the stop aborts the connection and ends the client's
 * reconnect loop. Opening several instances by accident is safe: each owns
 * a separate connection (the backend supports several per user) and routes
 * events only to its own listeners, so there is no shared state to collide.
 * The authorized layout opens the one connection per page load and shares
 * it with subpages through context; subpages only register listeners.
 */
export class LiveStream {
	#abort: AbortController | null = null;
	#notification_listeners = new Set<NotificationListener>();
	#any_auction_listeners = new Set<AuctionUpdateListener>();
	#per_auction_listeners = new Map<string, Set<AuctionUpdateListener>>();

	/// Opens the connection. Idempotent while running; after `stop` it opens
	/// a fresh connection. No-op outside the browser.
	start(): void {
		if (!browser || this.#abort) {
			return;
		}
		const controller = new AbortController();
		this.#abort = controller;
		void this.#drain(controller);
	}

	/// Aborts the connection. Idempotent; a stale drain stops routing.
	stop(): void {
		this.#abort?.abort();
		this.#abort = null;
	}

	on_notification(listener: NotificationListener): () => void {
		this.#notification_listeners.add(listener);
		return () => this.#notification_listeners.delete(listener);
	}

	on_any_auction_update(listener: AuctionUpdateListener): () => void {
		this.#any_auction_listeners.add(listener);
		return () => this.#any_auction_listeners.delete(listener);
	}

	on_auction_update(auctionId: string, listener: AuctionUpdateListener): () => void {
		let listeners = this.#per_auction_listeners.get(auctionId);
		if (!listeners) {
			listeners = new Set();
			this.#per_auction_listeners.set(auctionId, listeners);
		}
		listeners.add(listener);
		return () => {
			listeners.delete(listener);
			if (listeners.size === 0) {
				this.#per_auction_listeners.delete(auctionId);
			}
		};
	}

	/// Routes one event to this stream's listeners. The pump calls this for
	/// every frame; public so tests can drive routing without a connection.
	handle(event: LiveEvent): void {
		if (event.kind === 'Notification') {
			const notification = event as LiveNotification;
			for (const listener of this.#notification_listeners) listener(notification);
			return;
		}

		const update = event as LiveAuctionUpdate;
		for (const listener of this.#any_auction_listeners) listener(update);
		this.#per_auction_listeners.get(event.auctionId)?.forEach((listener) => listener(update));
	}

	async #drain(controller: AbortController): Promise<void> {
		const client = build_browser_client();
		const { stream } = await client.getApiEventsStream({
			credentials: 'same-origin',
			signal: controller.signal
		});
		// The generated ServerSentEventsResult maps the stream element type
		// through `TData[keyof TData]` in this hey-api release, mangling object
		// payloads; the runtime yields parsed LiveEvent objects, hence the
		// documented cast. The stream reconnects with exponential backoff and
		// ends once the signal is aborted; a drain superseded by stop/restart
		// stops routing instead of double-delivering.
		for await (const event of stream as AsyncGenerator<LiveEvent, void, unknown>) {
			if (this.#abort !== controller) {
				return;
			}
			this.handle(event);
		}
	}
}
