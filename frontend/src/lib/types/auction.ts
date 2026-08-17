export type AuctionSummary = {
	id: string;
	title: string;
	description: string;
	minimumPrice: number;
	closureTime: string;
	isClosed: boolean;
	imageUrls: string[];
	currentBid: number | null;
};

export type AuctionDetail = AuctionSummary;

export function default_auction(): AuctionSummary {
	return {
		id: '',
		title: '',
		description: '',
		minimumPrice: 0,
		closureTime: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
		isClosed: false,
		imageUrls: [],
		currentBid: null,
	};
}
