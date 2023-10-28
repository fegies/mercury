create table auction_item(
    id uuid primary key not null default gen_random_uuid()
    , item_name text not null
    , description text null
);

create table auctions(
    id uuid primary key not null default gen_random_uuid()
    , item_id uuid not null references auction_item(id) on delete cascade
    , end_time timestamptz not null
    , multiplicity int not null
    , minimum_bid numeric(20,1) not null
    , constraint multiplicity_gt_0 check(multiplicity > 0)
);

create table bids(
    id serial primary key not null
    , auction_id uuid not null references auctions(id) on delete cascade
    , creator_id uuid null references users(user_id) on delete set null
    , modification_time timestamptz not null default now()
    , current_value numeric(20, 1) not null
    , max_value numeric(20, 1) not null
    , constraint max_ge_current check(max_value >= current_value)
);
create index idx_bids_action_id_ranking_idx on bids(auction_id, max_value desc, modification_time asc);
create index idx_bids_creator_id on bids(creator_id);

create function raise_bid(p_bid_id int, p_bid_value numeric(20,1))
returns table(value numeric(20,1))
language plpgsql
as $$
declare v_multiplicity int;
declare v_auction_id uuid;
declare v_previous_value numeric(20,1);
begin
    select a.id, a.multiplicity, b.max_value
    from auctions a
    inner join bids b
    on a.id = b.auction_id
    where b.id = p_bid_id
    into v_auction_id, v_multiplicity, v_previous_value;

    if not found then
        raise 'invalid auction or bid';
    end if;

    if p_bid_value < v_previous_value then
        raise 'cannot lower a previous bid';
    elsif p_bid_value > v_previous_value then
        update bids
        set 
            max_value = p_bid_value
            , modification_time = now()
        where bids.id = p_bid_id;
    end if;

    -- now we need to see if there is actually a need to fight.
    -- if there are fewer bids than there are items, they will just
    -- be sold at the minimum bid values.

    if (
        select count(*) > v_multiplicity from bids b
        where b.auction_id = v_auction_id
    ) then
        -- determine the maximum amount the bidder who will not get
        -- his item anymore was willing to pay
        select b.max_value + 0.5 from bids b
        where b.auction_id = v_auction_id
        order by max_value desc
        limit 1 offset v_multiplicity
        into v_previous_value;
        
        -- raise the real prices to this level
        update bids
        set current_value = least(v_previous_value, max_value)
        where auction_id = v_auction_id
        and current_value < max_value;
    end if;

    -- get the pricelist for the currently winning bidders
    return query
    select b.current_value
    from bids b
    where auction_id = v_auction_id
    order by b.max_value desc, b.modification_time asc
    limit v_multiplicity;
end;
$$;

create function place_bid(p_user_id uuid, p_auction_id uuid, p_bid_value numeric(20,1))
returns table(value numeric(20,1))
language plpgsql
as $$
declare v_cond boolean;
declare v_multiplicity int;
declare v_min_value numeric(20,1);
begin
    select a.multiplicity, a.minimum_bid
    from auctions a
    where a.id = auction_id
    into v_multiplicity, v_min_value;

    if not found then
        raise 'Invalid auction';
    end if;

    if p_bid_value < v_min_value then
        raise 'cannot bid lower than minimum value';
    end if;

    if (
        select count(*) >= v_multiplicity
        from bids b
        where b.creator_id = p_user_id
        and b.auction_id = p_auction_id
    )
    then
        raise 'cannot place more bids than items exist';
    end if;

    -- now we are free to actually insert our new bid.
    -- However, since it may actually alter the effective values,
    -- we need to execute the value raising logic in raise_bid too.
    return query
    with new_id(bid_id) as (
        insert into bids(auction_id, creator_id, current_value, max_value)
        values (p_auction_id, p_user_id, v_min_value, p_bid_value)
        returning id
    )
    select raise_bid(n.bid_id, p_bid_value)
    from new_id n;
end;
$$;
