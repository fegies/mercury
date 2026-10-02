/**
 * Tracks which snapshot of an auction's bid state is the fresher one: the
 * user's own bid response or the last live refetch. Neither can win
 * statically — the bid response beats a refetch that completed before it,
 * and a refetch completed after the bid beats it back.
 */
export class BidRecency {
	#next = 1;
	#bid_mark = $state(0);
	#live_mark = $state(0);

	/// Called when the user's own bid response arrives.
	mark_bid(): void {
		this.#bid_mark = this.#next++;
	}

	/// Called when a live refetch completes.
	mark_live(): void {
		this.#live_mark = this.#next++;
	}

	/// True while the live refetch is at least as fresh as the bid response.
	get live_is_fresher(): boolean {
		return this.#live_mark >= this.#bid_mark;
	}
}
