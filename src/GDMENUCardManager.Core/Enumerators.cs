namespace GDMENUCardManager.Core
{
    public enum WorkMode
    {
        None,
        New,
        Move
    }

    public enum FileFormat
    {
        Uncompressed,
        SevenZip
    }

    public enum SpecialDisc
    {
        None,
        CodeBreaker,
        BleemGame
    }

    public enum RenameBy
    {
        Ip,
        Folder,
        File,
    }

    public enum MenuKind //folder name must match the enum name. case sensitive.
    {
        None,
        gdMenu,
        openMenu
    }
}