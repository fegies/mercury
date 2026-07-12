begin;

drop schema if exists logic cascade;

create schema logic;

-- place or raise a bid on the provided auction for the given user.
-- returns the new price of the item.
create function logic.place_or_raise_bid(p_user_id uuid, p_auction_id uuid, p_bid_value numeric(20,1))
returns numeric(20,1)
language plpgsql
as $$
declare v_min_value numeric(20,1);
declare v_effective_value numeric(20,1);
begin
    select a.minimum_bid
    from auctions a
    where a.id = p_auction_id
    into v_min_value;

    if not found then
        raise 'Invalid auction';
    end if;

    if p_bid_value < v_min_value then
        raise 'cannot bid lower than minimum value';
    end if;

    if exists(
        select 1 from bids
        where auction_id = p_auction_id
        and creator_id = p_user_id
        and p_bid_value <= max_value
    )
        raise 'cannot lower a previous bid'
    end if;

    -- and actually place or raise the bid.

    
end;
$$;

commit;