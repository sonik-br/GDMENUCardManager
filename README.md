# GDMENU Card Manager
Manage content on SD Card for Dreamcast's GDEmu/GDMenu/openMenu

For GDEMU to properly work it's required to follow a pattern when putting files on the SD Card.<br/>
If not done correctly it will take a lot of time to boot and can even fail to.<br/>
This program will save the files the proper way so you console will boot super fast!

![Main window](docs/capture1.png)
![Info window](docs/capture2.png)


## Features
* Multi platform. Windows/Linux/OSX
* Supports both GDmenu and openMenu
* Supports GDI, CDI, MDS, CCD, CHD and CUE/BIN files. Also compressed files inside zip/rar/7z
* Add/delete/rename items
* Sort alphabetically
* Sort manually by drag and drop
* Automatically rename based on folder name, file name or internal name (IP.BIN)
* Show cover image (0GDTEX.PVR)
* If using a CodeBreaker image it can detect if it's the correct one.
* Saves name.txt on each folder to keep compatibility with other managers.
* Menu is built in GDI format. Compatible with consoles that cant boot MIL-CD.
* GDI Shrinking. Can reduce file size by removing dummy data without reducing the quality.

### GDI Shrinking
Can reduce the size of the game but some games won't work properly.

The program have a blacklist with some games known to have problems after shrinking.

By default the blacklist will be used and those games will not be shrunk.

### openMenu
openMenu will display customized icon, box art and text info for each title, but it requires additional files.

openMenu DAT files should go to this app's subfolder `tools\openMenu\menu_data`.<br/>
Grab the DAT files from mrneo240's repos: [imagedb](https://github.com/mrneo240/openMenu_imagedb) and [metadb](https://github.com/mrneo240/openMenu_metadb)

### Windows version requires the .Net 8 Desktop Runtime
Download and install the [Desktop Runtime installer](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) for your system.
![Dot Net Runtime](docs/dotnetruntime.png)

### Limitations
**If you can't run it on OSX please check issue #4 for a workaround**

### Building
When building for Linux or macOS, set the executable permission (`chmod +x`) on `GDMENUCardManager` and `tools/redump2cdi`.

- Linux x64 via CLI
  - ensure dotnet sdk 8.0 is installed via  [Install .NET on Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux)
  - clone repository
  - Build & Publish
    - Simple
      - execute `dotnet publish GDMENUCardManager.AvaloniaUI/GDMENUCardManager.AvaloniaUI.csproj -c Release -r linux-x64`
    - Single File Self Contained (more portable, includes dotnet runtime bundled in)
      - execute `dotnet publish GDMENUCardManager.AvaloniaUI/GDMENUCardManager.AvaloniaUI.csproj -c Release --self-contained true -r linux-x64  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
  - Execute `GDMENUCardManager` in `GDMENUCardManager/src/GDMENUCardManager.AvaloniaUI/bin/Release/net8.0/linux-x64/publish`
    -via double click or by running `./GDMENUCardManager` or by `dotnet ./GDMENUCardManager`

### Building for MacOS
  #### Install dependencies
  ##### Runtime is required to run the application and dotnet is required to build the application.

  - install .NET Runtime 8.0 from [here](https://dotnet.microsoft.com/download/dotnet/8.0/runtime)
  - `brew install dotnet`
  #### Build
  ##### Navigate to the `src` folder and build the project using dotnet publish. Use `-r osx-arm64` on Apple Silicon Macs or `-r osx-x64` on Intel Macs.
  ```bash
  $ cd src/
  $ dotnet publish GDMENUCardManager.AvaloniaUI/GDMENUCardManager.AvaloniaUI.csproj -c Release -r osx-arm64
  ```
  #### Run
  ##### Navigate to the publish folder and run the application.
  ```bash
  $ cd GDMENUCardManager.AvaloniaUI/bin/Release/net8.0/osx-arm64/publish/
  $ ./GDMENUCardManager
  ```

### Credits
This software is made possible by using third party tools:

[GDEMU](https://gdemu.wordpress.com),
[GDmenu](https://assemblergames.org/viewtopic.php?t=49106&start=190),
[openMenu](https://github.com/mrneo240/openmenu/),
[GdiTools](https://sourceforge.net/projects/dcisotools/),
[GdiBuilder](https://github.com/Sappharad/GDIbuilder/),
[Aaru](https://github.com/aaru-dps/Aaru/),
[PuyoTools](https://github.com/nickworonekin/puyotools/),
[7-zip](https://www.7-zip.org/),
[SevenZipSharp](https://github.com/squid-box/SevenZipSharp/),
[SharpCompress](https://github.com/adamhathcock/sharpcompress/),
[libchdr](https://github.com/rtissera/libchdr/),
[gdidrop](https://github.com/feyris-tan/gdidrop),
[Redump2CDI](https://dreamcast.wiki/Redump2CDI)

Some features backported from Derek Pascarella's (ateam) [GD MENU Card Manager fork](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/tree/main/GD%20MENU%20Card%20Manager)

Special thanks to megavolt85 and everyone in the dreamcast scene
