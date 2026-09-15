# SS14.Admin deployment

Config scaffold for running [SS14.Admin](https://github.com/space-wizards/SS14.Admin)
against the StarHorizon game server, on the `starhorizon.ru` domain.

SS14.Admin connects directly to the same PostgreSQL database as the game
server (`[database]` section in `Resources/ConfigPresets/StarHorizon/server_config.toml`)
and lets admins log in with their SS14 account via OAuth. It is a separate
deployment from the game server itself — this directory only holds the
config scaffold, it is not built or shipped as part of the game.

## Steps

1. **Create a dedicated Postgres role** for SS14.Admin in the `ss14`
   database (don't reuse the game server's `ss14_user`):
   ```sql
   CREATE USER ss14_admin WITH PASSWORD '...';
   GRANT ALL PRIVILEGES ON DATABASE ss14 TO ss14_admin;
   ```

2. **Register an OAuth app** at
   https://account.spacestation14.com/Identity/Account/Manage/Developer →
   "New OAuth App":
   - Authorization callback URL: `https://admin.starhorizon.ru/signin-oidc`
   - Homepage URL: `https://admin.starhorizon.ru`
   - Copy the Client ID / generate a secret (shown only once).

3. **Copy the config template and fill in secrets:**
   ```sh
   cp appsettings.example.yml appsettings.yml
   # edit appsettings.yml: DB password, ClientId, ClientSecret
   ```
   `appsettings.yml` is gitignored — never commit it, it holds live secrets.

4. **Attach to the game server's docker network.** `docker-compose.yml`
   expects an external network named `starhorizon_default` — replace that
   with whatever network the `database` (Postgres) container actually runs
   on (check with `docker network ls` / `docker inspect <postgres_container>`).

5. **Start it:**
   ```sh
   docker compose up -d
   ```
   SS14.Admin listens on `127.0.0.1:27689` (mapped from the container's
   port 8080), not exposed publicly — the reverse proxy in front of it
   handles TLS.

6. **Point a reverse proxy at it** for `admin.starhorizon.ru`, using
   `Caddyfile.example` or `nginx.conf.example` as a starting point. HTTPS is
   required — OAuth login will not work over plain HTTP.

7. **DNS**: add an `A`/`AAAA` record for `admin.starhorizon.ru` pointing at
   the box running the reverse proxy.

Only accounts that already have admin rows in the game server's database
(or are configured as host admin via `login_host_user` in
`server_config.toml`) will have any permissions once logged in — OAuth only
grants login, not admin rights.

See the [official setup guide](https://docs.spacestation14.com/en/server-hosting/setting-up-ss14-admin.html)
for the full reference and troubleshooting.
