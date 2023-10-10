-- Your SQL goes here

create table app_config(
    id serial primary key not null
    , deployment_domain text not null
);

create table oauth_providers(
    name text primary key not null
    , auth_url text not null
    , token_url text not null
    , client_id text not null
    , client_secret text not null
);