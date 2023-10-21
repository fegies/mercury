-- Your SQL goes here

create table users(
    user_id uuid primary key not null default gen_random_uuid()
    ,display_name text not null
    ,preferred_username text not null
    ,can_start_auctions boolean not null default false
);

create table profile_pics(
    user_id uuid primary key not null references users(user_id) on delete cascade
    , hash uuid not null
    , mime_type text null
    , picture bytea not null
);

create table external_users(
    internal_user uuid not null references users(user_id) on delete cascade
    , issuer text not null
    , issuer_sub text not null
    , constraint external_users_pk primary key(issuer, issuer_sub)
);
