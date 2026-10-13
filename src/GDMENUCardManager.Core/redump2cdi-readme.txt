Redump2CDI 1.00
2025-09-10
https://dreamcast.wiki/Redump2CDI

Redump2CDI is a utility created by darc to convert Redump Dreamcast CD-ROM images to DiscJuggler format. The Redump image format is a non-standard form of BIN+CUE image that cannot be burned or mounted with much existing software, so conversion is convenient for non-archival purposes.

It can also convert Redump bin/cue images for other systems as well, although this is not its primary intended use case.

Redump2CDI is intended only for Redump Dreamcast CD-ROM images. It is not intended to convert GD-ROM images or old schoool non-selfboot BIN+CUE Dreamcast files.

CHANGELOG:
1.00: As requested by several users, added ability to ignore unsupported FLAGS, CDTEXTFILE, PERFORMER, SONGWRITER, and TITLE cuesheet commands with a warning about inaccuracy, though images using these flags should work fine. These aren't used by any Redump Dreamcast images, but this allows redump2cdi to be used for Redump images for other consoles. Added `-u` flag to skip other unsupported, unrecognized commands at user's own risk.
0.9.0: Added support for processing MODE1/2352 tracks. These aren't used in Dreamcast images, but support for these tracks was requested as it helps this utility be useful for converting other systems' images, too.
0.8.0: Largely refactored code. Not publicly released.
0.1.0: Initial release.
