# Deploying OXY after a round

## One-time setup

The running server must have the round-end deployment commands installed before it can accept a queued update. For the first installation, wait for the round to finish, stop the server in OXY, then run **OXY Manual Build** on **main** with **deploy** selected. The workflow can install directly when the server is already offline.

Do not stop a live round to perform this setup. An older running server will reject the new command, and the workflow will fail without replacing its game files.

## Future updates

1. Merge the approved changes into **main**.
2. Open the repository's **Actions** page.
3. Select **OXY Manual Build**, then **Run workflow**.
4. Select **main** and the **deploy** mode, then run it once.
5. The workflow builds and stages the package while the current server runs.
6. Once the package is ready, players receive an announcement that it will be installed after the round.
7. The game finishes its current round, writes a confirmation, and holds at that boundary. The workflow then requests an intentional OXY stop and waits for its offline state. This prevents OXY's automatic process recovery from racing installation.
8. The workflow installs the package, preserves configuration/data/logs, verifies executable permissions, and starts OXY again.
9. Check the workflow result and connect to the server to verify the update.

If the server is in the lobby or the round has already ended, it can shut down after a short announcement delay. A second run queues behind the first; avoid submitting duplicate deployments.

## Cancellation and failures

- While waiting for the round, a cancelled or disconnected workflow's request expires within three minutes. The current round is not force-stopped.
- Waiting is limited to five hours. If no safe boundary is confirmed, the workflow fails without installing the package.
- An unexpected server crash is not treated as confirmation that the round ended.
- Once installation begins, let it finish. If it is interrupted, keep OXY offline until installation or recovery is completed. The workflow summary lists recovery folders.
- The existing recovery routine attempts to restore the previous package if installation or startup fails.

The workflow remains manual. Merging a PR does not deploy it automatically.
