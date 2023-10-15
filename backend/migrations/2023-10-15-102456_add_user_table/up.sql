-- Your SQL goes here

create table users(
    user_id uuid primary key not null default gen_random_uuid()
    ,display_name text not null
    ,preferred_username text not null
);

create table profile_pics(
    user_id uuid primary key not null references users(user_id) on delete cascade
    , picture bytea not null
);

create table external_users(
    internal_user uuid not null references users(user_id) on delete cascade
    , issuer text not null
    , issuer_sub text not null
    , constraint external_users_pk primary key(issuer, issuer_sub)
);
create index external_users_issuer_subject_idx on external_users(issuer, issuer_sub);