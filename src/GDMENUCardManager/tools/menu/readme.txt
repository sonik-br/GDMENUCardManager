   _____ _____                             
  / ____|  __ \                            
 | |  __| |  | |_ __ ___   ___ _ __  _   _ 
 | | |_ | |  | | '_ ` _ \ / _ \ '_ \| | | |
 | |__| | |__| | | | | | |  __/ | | | |_| |
  \_____|_____/|_| |_| |_|\___|_| |_|\__,_|
============================================
Version : v0.6.0
Date	: August 24, 2016
Author  : neuroacid
Contact : n3uroacid(at)gmail.com


What's new in this version?
============================================
 - Improved the overall responsiveness and browsing speed.
 - Implemented support for japanese_cake's custom bootROM v1.032 in the bootloader.
 - Implemented analog stick support for page scrolling and menu highlighting.
 - Implemented faster page scrolling using the left and right analog triggers.
 - Added an options menu to toggle the various patches on/off.
 - Added a system information menu with GDEMU firmware version and other specifics.
 - Added CodeBreaker disc image detection and prompt to select a game to load with it.
 - On-the-fly patching of CodeBreaker for it to read and boot disc images in MIL-CD format.
 - On-the-fly patching of CodeBreaker to make it compatible with japanese_cake's bootROM v1.032.
 - Other minor changes and fixes.


About this software
============================================
GDmenu is an homebrew for the Sega Dreamcast to be used with the GDEMU ODE and to make its operation easier.
For more information on the GDEMU ODE visit the following page: https://gdemu.wordpress.com/about

The purpose of the software is to generate a list of games on the SD card and show it in an easy to use menu
making it simple to select the game you want to play. Additionally it also includes some nice extra features.


How to install
============================================
GDmenu software is very easy to set-up, the only requirement for correct operation is having the appropriate
firmware update installed on your GDEMU. In case you are unsure which update version you might have currently
just follow the steps below and after booting it will warn you if a firmware update is necessary.
Get it from https://gdemu.wordpress.com/firmware/updating-gdemu and follow the instructions on that page.

Follow these simple steps to set it up:
 - Upon extracting the GDmenu zip archive, copy the file GDEMU.ini and folder 01 to the root of your SD card.
 - In case folder 01 already exists in the root of the SD card remember to move the contents elsewhere first.
 - Eject the SD card from your reader, insert in your GDEMU SD slot and power-on your Dreamcast.
 - After booting the software, a loading screen will appear while the games list is being generated.
 - Finally, if everything went according to plan the menu is displayed, listing all the games on the SD card.


How to use
============================================
 - Up/Down on the D-pad or analog stick to browse through the list of games or highlight options.
 - (A) button to confirm/accept the highlighted selection be it to start a game or change settings.
 - (B) button to cancel selection or return from a menu.
 - (X) button to bring up the menu for selecting various options or view hardware information.
 - (Y) button to exit from the GDmenu software back to the BIOS system menu.


Features
============================================
Region Free:
 - Boot games from any region without any additional hardware modifications! (see note *1)

Force VGA:
 - Play games in VGA video mode which would otherwise need the VGA flag to be patched! (see note *2)

In-Game Reset:
 - Reset your console without getting up for a manual reset, simply by holding A+B+X+Y+START on the controller
   while sitting at the game's main menu!

Boot intro animation skip:
 - Toggle off the BIOS boot intro animation for a quicker boot time into your games! (see note *3)

SEGA license screen skip:
 - Toggle off the SEGA license screen for a quicker boot time into your games! (see note *3)

CodeBreaker detection:
 - Detects the CodeBreaker disc image and prompts you to select a game to load with it, no need to stand up to
   change disc images whenever you want to use cheat codes with your games! (see note *4)


Notes
============================================
Note *1:
 - This skips the regional checks performed by the BIOS during boot (similar to what a region free BIOS does).
   However some games themselves can have additional protections but fortunately this also patches the console
   region to match the game, so as far as the game is concerned your console is always the appropriate region.
   For the system region patching to work properly, it is imperative that you do not make any modifications to
   the game's IP.BIN region flags, as patching the system region relies on those being in their original state.

Note *2:
 - This skips the VGA flag check performed by the BIOS during boot, which in turn allows games that have proper
   VGA output support (but no VGA flag set in the AIP), to boot correctly while avoiding the following message
   from being shown: "This game doesn't support the AV cable currently connected..."
   However a small percentage of games lack the support the VGA video mode which means that in those cases this
   makes no difference and the game's binary has to be directly patched on case by case basis.

Note *3:
 - At the moment, due to what seems to be problem specific to the GDEMU, most disc images in the MIL-CD format
   won't boot when skipping the BIOS boot intro animation. So for now, while the problem is being investigated
   toggling boot intro and Sega license off only has an effect on .GDI disc images (full GD-ROM dumps).
   
Note *4:
 - For this to work correctly, the right version of the CodeBreaker has to be detected which means that some of
   the information present in its IP.BIN should match what is shown in the GDmenu info pane.
   So while in the menu with its entry highlighted the pane on the right should match the following information
   "DISC                  "
   "DATE        2000/06/27"
   "VERSION         V1.000"
   Additionally the binary has to be called PELICAN.BIN and have the md5 hash: cc0b1e71a61587b0213fd5424b14eb22

For the optimal usage of the software, it is essential to copy the GDEMU.ini file to the root of your SD card.
So it should contain at least the following three lines inside:
open_time = 150
detect_time = 150
reset_goto = 1

First two lines guarantee quicker browsing speeds, especially when loading the cover images, the third line is
necessary to return to the GDmenu after a reset (by pressing A+B+X+Y+START while in-game).


Changelog
============================================
v0.5.2 (14/07/2015):
 - Implemented region free and VGA flag check patches for bootROM v1.004.

v0.5.1 (14/06/2015):
 - Fixed an issue with the video mode on PAL systems when using RGB/Composite.

v0.5.0 (01/06/2015):
 - Updated the internal bootloader.
 - Added region check patch (ignore checking of region flags).
 - Added VGA flag check patch (ignore checking of VGA flag).
 - Added in-game reset patch (reset back to GDmenu by holding A+B+X+Y+START while in-game).

v0.4.0 (08/02/2015):
 - Increased games list maximum size to 999.
 - Fixed list scrolling de-sync issue that could happen on slower speed SD cards.
 - Removed wrap around from first to last game on the list (not supported anymore by firmware).

v0.3.0 (17/10/2014):
 - Overall re-design and improvements to the menu GUI.
 - Now shows additional information about the selected disc image on the right.
 - Now displays the '0GDTEX.PVR' texture (cover image) when present on the disc.
 - Fixed a problem where disc images with a high number of tracks could cause a de-sync in the list.
 - Added workaround for disc images with no title on 'IP0000.BIN', parse volume descriptor instead.

v0.2.0 (15/09/2014):
 - Now supports sending Up/Down inputs to the GDEMU via emulation API. 
 - Now automatically parses and loads the list of games on the SD card upon booting.
 - Removed the VMU save/load functions as they are no longer useful.
 - Removed multi-list browsing as it's no longer necessary.
 - Added initial support to load the list of games from a .ini file.
 - Improved menu GUI with better font and rendering.
 - Other minor bug-fixes.

v0.0.1 (23/08/2014):
 - Initial public release.


Special Thanks
============================================
In no particular order:
 - Deunan for creating the GDEMU and the great work so far supporting it with firmware updates.

 - Dan Potter & Company for starting the KallistiOS project among all other contributions to get the Dreamcast
   homebrew scene started.

 - Marcus Comstedt for all his past contributions to the scene and in particular for the wealth of information
   about the Dreamcast hardware that is available to this day on his site.

 - Lawrence Sebald for being always very helpful and maintain the KallistiOS alive.

 - drk|Raziel & Company for creating the NullDC emulator which to this day is still a very helpful tool and in 
   particular the debugger which is invaluable.

 - Lars Olsson for all the great work reversing the bootROM and hardware registers.

 - Japanese_cake for his cool custom bootROM which thanks to a very clever use of the beta bootstrap opens up 
   a lot of possibilities for the future.

 - Nightbreed for the exhaustive work of testing the software to make sure everything works as intended.