# PokeChess account API release

This is a framework-dependent .NET 10 account server. It is not deployed and contains no production account DB or UGS service keys. The Unity client is built separately.

## Verify and install

1. Obtain the archive SHA-256 through a trusted channel and compare it before extraction. The adjacent .sha256 and manifest detect corruption; they are not signatures or proof of publisher identity.
2. Extract into a new release directory. With PowerShell 7 run `pwsh -File VerifyRelease.ps1 -ReleaseDirectory .`. Do this before adding local files. A modified or missing file must fail verification.
3. Install the latest .NET 10 ASP.NET Core runtime for the archive's OS/CPU. linux-x64 and linux-arm64 require a compatible glibc Linux distribution; they are not Alpine/musl builds. win-x64 is for Windows verification and cannot use the Linux service example.
4. On Linux create a dedicated pokechess user and /var/lib/pokechess (0700). Put this release's app directory at /opt/pokechess-account, or point that path to the selected release's app directory. Keep all previous release files immutable. State lives outside the release at /var/lib/pokechess/accounts.db.
5. Copy account.env.example to /etc/pokechess/account.env with 0600 access. Replace the example origin/host and supply Ugs__ProjectId, Ugs__EnvironmentId, Ugs__EnvironmentName, Ugs__KeyId and Ugs__SecretKey. Never put these in the release or Git. Configure nginx using nginx.conf.example with the real domain and certificate. TrustedProxies must match the actual single-hop proxy; block direct external Kestrel access.
6. Install pokechess-account.service.example as the systemd unit, run nginx configuration validation, then start the service and nginx. Keep the app and /auth on the same HTTPS origin. Set the separately built Unity client's AccountApiSettings.BaseUrl to this address.
7. Before public use, check HTTPS certificate, login/logout/recovery, service restart, same account on two devices, UGS room join/reconnect and backup restoration. These require real hosting and UGS settings and are not certified by the package tests.

## Backup and rollback

Create a protected backup before upgrades:

`dotnet /opt/pokechess-account/AccountApi.dll backup /var/lib/pokechess/accounts.db /protected-backup/new-name.db`

Keep a verified previous app release. Stop the service, point /opt/pokechess-account to that previous app directory, then start and verify login. Code rollback does not undo database migrations: review schema compatibility first. Do not automatically replace the live DB during code rollback.

For DB recovery, stop the service, preserve the current DB, copy a verified backup to a new DB path, update AccountDb and restart. Never mix stale -wal/-shm files with a restored DB. Restoring old data can revive old sessions/passwords/recovery codes; incident recovery requires explicit session invalidation and review. External encrypted backup scheduling and retention must be configured by the operator.

Source publishing reference: https://learn.microsoft.com/en-us/dotnet/core/deploying/
