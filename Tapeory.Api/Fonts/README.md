# Bundled fonts

These Google Fonts ship with Tapeory, so they're available for labels on every machine
regardless of what the operating system has installed (see `Rendering/BundledFonts.cs`). Each
family comes as Regular and Bold, taken unchanged from Debian's packages, whose copyright file is
kept as `COPYRIGHT` in the family's folder.

| Family | Folder | Debian package | License |
| --- | --- | --- | --- |
| Roboto | `Roboto` | fonts-roboto-unhinted | Apache 2.0 |
| Open Sans | `OpenSans` | fonts-open-sans | Apache 2.0 |
| Lato | `Lato` | fonts-lato | SIL OFL 1.1 |
| Montserrat | `Montserrat` | fonts-montserrat | SIL OFL 1.1 |
| Inter | `Inter` | fonts-inter | SIL OFL 1.1 |
| IBM Plex Sans | `IBMPlexSans` | fonts-ibm-plex | SIL OFL 1.1 |
| IBM Plex Serif | `IBMPlexSerif` | fonts-ibm-plex | SIL OFL 1.1 |
| Roboto Slab | `RobotoSlab` | fonts-roboto-slab | Apache 2.0 |
| Bebas Neue | `BebasNeue` | fonts-bebas-neue | SIL OFL 1.1 |
| League Spartan | `LeagueSpartan` | fonts-league-spartan | SIL OFL 1.1 |
| Quicksand | `Quicksand` | fonts-quicksand | SIL OFL 1.1 |
| Comic Neue | `ComicNeue` | fonts-comic-neue | SIL OFL 1.1 |
| Fira Code | `FiraCode` | fonts-firacode | SIL OFL 1.1 |

The full license texts are in [`licenses/`](licenses/). To add a family, drop its `.ttf` or
`.otf` files and its license into a new folder here; they're picked up on startup.
