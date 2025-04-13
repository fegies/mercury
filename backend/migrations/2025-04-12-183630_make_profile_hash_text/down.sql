-- This file should undo anything in `up.sql`

truncate table profile_pics;

alter table profile_pics alter column hash type uuid using gen_random_uuid();

