export type DetailedAuction = {
    id: string,
    name: string,
    description: string,
    image_urls: string[],
    min_price: number,
    auction_end: Date,
};

export function default_auction(): DetailedAuction {
    return {
        id: "",
        name: "",
        description: "",
        min_price: 0,
        image_urls: [],
        auction_end: new Date(new Date().getDate() + 7 * 24 * 60 * 60 * 1000),
    };
}