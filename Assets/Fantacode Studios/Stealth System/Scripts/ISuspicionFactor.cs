namespace FS_StealthSystem
{
    public interface ISuspicionFactor
    {
        bool IsActive { get; }
        bool IsInstant { get; }
        bool WarnsFirst { get; }
    }
}
