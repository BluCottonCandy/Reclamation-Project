# Common Wealth mapping base

Map prototype ID: `CommonWealth`.
Map file: `Resources/Maps/Reclamation/CommonWealth.yml`.

This is a blank 1,000 by 500 tile wasteland ground rectangle, centered on (0, 0). Ground covers x = -500 through 499 and y = -250 through 249. Everything beyond that rectangle is space. There is no biome generation, scenery, buildings, or perimeter wall. Place the perimeter before using this as a player map.

A Wastelander spawn marker sits at the center. The map has gravity, warm daylight, and a static breathable atmosphere. Keep `misfits.atmos_simulated` set to `false` on the mapping server to disable atmosphere simulation. Outside tiles remain space visually; the static atmosphere is map-wide. This base is registered for manual loading but is not added to the normal map rotation.

## Load for editing

1. Update your repository after this map's PR is merged.
2. Start your local server and client and log in with local administrator access.
3. Open the in-game console and enter `mapping 9 /Maps/Reclamation/CommonWealth.yml`. Use another unused number if map 9 already exists. This enters mapping mode and loads the base for editing.
4. Use the entity and tile placement tools to build. Save frequently.
5. Save your map with `savemap 9 CommonWealth.yml`. If replacing an existing saved file, use `savemap 9 CommonWealth.yml true`.
6. Copy the saved file from the server's data directory back to `Resources/Maps/Reclamation/CommonWealth.yml` in your checkout, inspect your changes, and submit a PR on your own branch.

Map 9 is only an example runtime ID. It is not a permanent map identifier and does not synchronize separate computers. Coordinate who edits which version; merging two independently saved map files can be difficult. For simultaneous building, everyone must connect to one shared mapping server, with one person responsible for saving and publishing.

## Test as the starting map

Start the server with `--cvar game.map=CommonWealth --cvar game.map_rotation=false --cvar game.lobbyenabled=false --cvar misfits.atmos_simulated=false`. Connect your client to that server. Select Wastelander to spawn at the center if presented with a job selection.

The map can be expanded later with the tile placement tools. Its starting dimensions do not impose an invisible boundary.
