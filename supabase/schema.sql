-- FocusGuard: Supabase 同期用スキーマ & RPC 関数
-- Supabase ダッシュボードの「SQL Editor」にそのまま貼り付けて Run してください。

create table if not exists public.focus_guard_states (
    sync_key text primary key check (char_length(sync_key) >= 8),
    state jsonb not null,
    updated_at timestamptz not null default now()
);

-- RLS を有効化し、anon からの直接 SELECT / INSERT / UPDATE をすべて拒否します。
-- 読み書きは sync_key を知っているクライアントのみ以下の SECURITY DEFINER RPC 経由で行えます。
alter table public.focus_guard_states enable row level security;

revoke all on table public.focus_guard_states from anon, authenticated;

-- 1. 状態取得 RPC
create or replace function public.focus_guard_get(p_sync_key text)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_row record;
begin
    if p_sync_key is null or char_length(trim(p_sync_key)) < 8 then
        raise exception 'sync_key must be at least 8 characters';
    end if;

    select state, updated_at
      into v_row
      from public.focus_guard_states
     where sync_key = trim(p_sync_key);

    if not found then
        return null;
    end if;

    return jsonb_build_object(
        'state', v_row.state,
        'updated_at', v_row.updated_at
    );
end;
$$;

-- 2. 状態保存 (UPSERT) RPC
create or replace function public.focus_guard_save(p_sync_key text, p_state jsonb)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_now timestamptz := now();
begin
    if p_sync_key is null or char_length(trim(p_sync_key)) < 8 then
        raise exception 'sync_key must be at least 8 characters';
    end if;

    insert into public.focus_guard_states (sync_key, state, updated_at)
    values (trim(p_sync_key), p_state, v_now)
    on conflict (sync_key)
    do update set
        state = excluded.state,
        updated_at = v_now;

    return jsonb_build_object(
        'ok', true,
        'updated_at', v_now
    );
end;
$$;

-- 3. 端末ハートビート (PC / Android の稼働状況のみ部分更新) RPC
create or replace function public.focus_guard_heartbeat(
    p_sync_key text,
    p_device text,
    p_status text,
    p_level int default 0
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_key text := trim(p_sync_key);
    v_now_ms bigint := (extract(epoch from now()) * 1000)::bigint;
    v_hb jsonb;
begin
    if v_key is null or char_length(v_key) < 8 then
        raise exception 'sync_key must be at least 8 characters';
    end if;
    if p_device not in ('pc', 'android') then
        raise exception 'device must be pc or android';
    end if;

    v_hb := jsonb_build_object(
        'lastSeen', v_now_ms,
        'status', coalesce(p_status, 'idle'),
        'effectiveLevel', coalesce(p_level, 0)
    );

    update public.focus_guard_states
       set state = jsonb_set(
               jsonb_set(state, '{devices}', coalesce(state->'devices', '{}'::jsonb), true),
               array['devices', p_device],
               v_hb,
               true
           )
     where sync_key = v_key;

    return jsonb_build_object('ok', true, 'lastSeen', v_now_ms);
end;
$$;

grant execute on function public.focus_guard_get(text) to anon, authenticated;
grant execute on function public.focus_guard_save(text, jsonb) to anon, authenticated;
grant execute on function public.focus_guard_heartbeat(text, text, text, int) to anon, authenticated;
