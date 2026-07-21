// Project: Daggerfall Unity iOS
// Purpose: Bridge the Addressables default-settings API into this editor namespace.

#if UNITY_EDITOR
using UnityEditor.AddressableAssets.Settings;

namespace DaggerfallUnityIOS.Editor
{
    internal static class AddressableAssetSettingsDefaultObject
    {
        internal static AddressableAssetSettings Settings =>
            UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
    }
}
#endif
