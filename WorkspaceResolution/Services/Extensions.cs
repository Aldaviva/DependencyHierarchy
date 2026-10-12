using System.Collections.Specialized;

namespace WorkspaceResolution.Services;

public static class Extensions {

    public static bool TryGetValue(this StringDictionary dict, string key, out string? value) {
        try {
            value = dict[key];
            return true;
        } catch (KeyNotFoundException) {
            value = null;
            return false;
        }
    }

    public static string? GetValueOrDefault(this StringDictionary dict, string key) {
        try {
            return dict[key];
        } catch (KeyNotFoundException) {
            return null;
        }
    }

}