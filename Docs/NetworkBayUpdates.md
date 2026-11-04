# Updating the NetworkBay server

The `NetworkBay Update` GitHub Actions workflow replaces `OXY Manual Build`.
It deploys only to the Reclamation Project server at NetworkBay. The old Oxy
server is retained for mapping and must not receive gameplay deployments.

## One-time setup

Create dedicated deployment credentials. Store the API token as the repository
Actions secret `NETWORKBAY_API_KEY` and the SFTP private key as
`NETWORKBAY_SSH_KEY`. Register only its public key with NetworkBay. Do not put
credentials in the repository or workflow logs. Prefer a dedicated account
restricted to this server if the host supports it.

Set these repository Actions variables:

| Variable | Value |
| --- | --- |
| `NETWORKBAY_PANEL_URL` | `https://panel.naidru.ca` |
| `NETWORKBAY_SERVER_ID` | `3f6a75df` |
| `NETWORKBAY_SFTP_HOST` | `800cc0300b7bc2e4.daemon.panel.gg` |
| `NETWORKBAY_SFTP_PORT` | `2022` |
| `NETWORKBAY_SFTP_USERNAME` | `nibanath22@gmail.com.3f6a75df` |
| `NETWORKBAY_SFTP_FINGERPRINT` | Independently verified ED25519 SHA256 host fingerprint |

Verify the fingerprint against the host or an already trusted SFTP connection.
Do not accept an unverified scan as the trust baseline. A changed or missing
fingerprint prevents authentication. The endpoint identity is also checked in
the workflow so a mistaken variable cannot redirect a deployment to Oxy.

## Run an update

Open **Actions â†’ NetworkBay Update â†’ Run workflow**, select **main**, then choose:

- **check**: read server status and verify SFTP key access. No file or power changes.
- **build**: build and retain a downloadable Linux server and bundled client.
- **stage**: build, upload, unpack and verify in an isolated staging directory.
  The running game is unchanged.
- **deploy**: stage first, then wait for the existing round-end deployment
  acknowledgement before stopping and replacing game files. An already offline
  server can be updated immediately. Successful deployment starts the server.

There is no automatic deployment on pushes or PR merges. Only stage and deploy
from main. The existing server-side round-end update commands are reused.

Packages exclude `server_config.toml`, player data, logs and other root database
or configuration files. Current Wendover map files and the bundled client are
required. Old game files are retained in a `.networkbay-rollback-*` directory.
Configuration, character data, bunker saves and computer records stay in place.

Do not cancel during file promotion. If a job is interrupted then, keep the
server offline and inspect its staging and rollback directories before starting
it. A normal caught failure attempts restoration automatically. Keep rollback
files until the updated game has been checked; review storage before removing
obsolete packages.

## Retire the Oxy updater

Disable `OXY Manual Build` immediately and remove its workflow when this change
is merged. Once NetworkBay passes its connection check, remove the obsolete
`OXY_*` GitHub secrets and variables and revoke the old deployment credentials
at Oxy. This does not delete the mapping server or its maps.
