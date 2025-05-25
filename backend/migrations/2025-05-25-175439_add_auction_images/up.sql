-- Your SQL goes here

create table auction_images(
    id serial primary key not null
    , auction_item_id uuid not null references auction_item(id) on delete cascade
    , hash text not null
    , content bytea not null
);
create index idx_auction_images_auction_item_id_idx on auction_images(auction_item_id);