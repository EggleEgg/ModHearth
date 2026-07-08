namespace ModHearth.Utilities;

/// <summary>
/// A set of structural keys (OBJECT_TYPE:ID) for every object defined by the
/// official Dwarf Fortress vanilla raws. This baseline is loaded once and used
/// to determine whether an asset originates from vanilla by set containment
/// rather than by string matching a hardcoded list.
/// </summary>
public sealed class VanillaRawBaseline
{
    public static VanillaRawBaseline Empty { get; } = new VanillaRawBaseline(Enumerable.Empty<ObjectKey>());

    private readonly HashSet<ObjectKey> _baseline;

    public VanillaRawBaseline(IEnumerable<ObjectKey> keys)
    {
        _baseline = new HashSet<ObjectKey>(keys);
    }

    public bool Contains(ObjectKey? key)
    {
        return key != null && _baseline.Contains(key);
    }

    public bool Contains(string objectType, string id)
    {
        if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(id))
            return false;

        return _baseline.Contains(new ObjectKey(objectType, id));
    }

    /// <summary>
    /// Loads the vanilla baseline by scanning the objects/ folders of all mods
    /// located under <paramref name="vanillaModsPath"/>.
    /// </summary>
    public static VanillaRawBaseline Load(string vanillaModsPath)
    {
        if (string.IsNullOrWhiteSpace(vanillaModsPath) || !Directory.Exists(vanillaModsPath))
            return Empty;

        try
        {
            List<ObjectKey> keys = new();

            foreach (string modDir in Directory.EnumerateDirectories(vanillaModsPath))
            {
                RawDatabase db = ModRawObjectScanner.Scan(modDir, Path.GetFileName(modDir));
                foreach (var kvp in db.DefinedObjects)
                {
                    string objectType = kvp.Key;
                    foreach (string id in kvp.Value)
                    {
                        keys.Add(new ObjectKey(objectType, id));
                    }
                }
            }

            return new VanillaRawBaseline(keys);
        }
        catch
        {
            return Empty;
        }
    }
}
