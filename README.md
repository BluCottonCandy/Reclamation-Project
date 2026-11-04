# Reclamation Project

**Reclamation Project** is a Fallout-themed, high-roleplay server for **Space Station 14**. This repository contains our server's code, maps, and content.

Join our community on [Discord](https://discord.gg/bxWtYSzZp) for announcements, development updates, and information about playing or contributing.

## Our roots

Reclamation Project is a fork of [Misfits: Nuclear Wasteland](https://github.com/Misfit-Sanctuary/nuclear-14). Misfits is an English-language fork of the Russian-language adaptation of **Nuclear 14**, originally created by Peptide90 in 2022 with community contributions.

Our project builds on that history, including work from Nuclear 14, Einstein Engines, Space Station 14, and the wider Fallout13/Space Station 13 community. Original contributors retain their credits and the applicable licenses for their work.

## Community and development

We develop Reclamation Project around our own wasteland setting, maps, and roleplay community. Bug reports, feedback, and contributions are welcome.

- [Reclamation Project Discord](https://discord.gg/bxWtYSzZp)
- [Report a bug](https://github.com/BluCottonCandy/Reclamation-Project/issues)
- [Contribute a change](https://github.com/BluCottonCandy/Reclamation-Project/pulls)

## Building and running locally

Install Git and the .NET 10 SDK. The SDK version and roll-forward policy are defined in [global.json](./global.json).

Clone this repository and initialize its engine submodule:

```sh
git clone --recurse-submodules https://github.com/BluCottonCandy/Reclamation-Project.git
cd Reclamation-Project
```

If you have already cloned it, initialize or update the submodules:

```sh
git submodule update --init --recursive
```

### Windows

Build and launch the local client and server:

```bat
Scripts\bat\buildAllDebug.bat
Scripts\bat\runQuickAll.bat
```

### Linux

```sh
sh Scripts/sh/buildAllDebug.sh
sh Scripts/sh/runQuickAll.sh
```

Connect to `localhost` in the client to use your local test server.

## Licensing and attribution

See [LEGAL.md](./LEGAL.md) for the full licensing details, asset credits, and exceptions. Code includes AGPLv3 and earlier MIT-licensed contributions; assets have their own licenses and attribution requirements. Some Reclamation maps have separate license notices.

Check the relevant license and metadata files before reusing code, maps, or artwork. This README does not change any existing licenses or ownership.
