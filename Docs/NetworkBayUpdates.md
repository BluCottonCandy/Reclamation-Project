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

Verify the fingerprint against the host or an already trusted SFTP connection,
or explicitly approve the first-seen key before pinning it. The current key was
approved by the server owner during setup. A changed or missing
fingerprint prevents authentication. The endpoint identity is also checked in
the workflow so a mistaken variable cannot redirect a deployment to Oxy.

## Run an update

Open **Actions â†’ NetworkBay Update â†’ Run workflow**, select **main**, then choose:

- **check**: read server status and verify SFTP key access. No file or power changes.
- **verify**: upload a tiny fixture to a separate `.networkbay-verify-*` folder
  and verify extraction, permissions and atomic SFTP moves. Does not replace
  live game files or change server power.
- **build**: build and retain a downloadable Linux server and bundled client.
- **stage**: build, upload, unpack and verify in an isolated staging directory.
  The running game is unchanged.
- **deploy**: stage first, then wait for the existing round-end deployment
  acknowledgement before stopping and replacing game files. An already offline
  server can be updated immediately. Successful deployment starts the server.

There is no automatic deployment on pushes or PR merges. Only verify, stage and deploy
from main. Deploy is manual only. With the server running, it sends one queue command
and checks the receipt without sending repeated console commands. The request
expires after five hours and three minutes. If the panel never stops the
server after it reaches the round boundary, the game releases the hold after
three minutes. Install the supporting server build once while offline before
using live round-end deployment.

Packages exclude `server_config.toml`, player data, logs and other root database
or configuration files. Current Wendover map files and the bundled client are
required. Old game files are retained in a `.networkbay-rollback-*` directory.
Configuration, character data, bunker saves and computer records stay in place.

Do not cancel during file promotion. If a job is interrupted then, keep the
server offline and inspect its staging and rollback directories before starting
it. A normal caught failure attempts restoration automatically. Keep rollback
files until the updated game has been checked; review storage before removing
obsolete packages.

## Retired Oxy updater

The old workflow was disabled and removed. Its `OXY_*` GitHub secrets and
variables have been removed after NetworkBay connection verification. The Oxy
mapping server and its maps are unaffected. Old credentials can also be revoked
at Oxy when they are no longer needed for any other purpose.

## Setup verification

API access and pinned SFTP key login passed. Isolated verification run
[41797038106](https://github.com/BluCottonCandy/Reclamation-Project/actions/runs/41797038106)
verified upload, extraction, executable permissions, directory pagination and
atomic file moves. This is not a full game deployment or rollback rehearsal.
The updater is pinned in the repository Actions sidebar for the owner.
