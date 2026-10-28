using System;
using System.IO;
using UnityEngine;
using BeefCity.Customization;

namespace BeefCity.Core
{
    [Serializable]
    public class SaveData
    {
        public string playerName = "Player";
        public CostumeData costume = CostumeData.CreateDefault();
        public float masterVolume = 1f;
        public bool fullscreen = true;
        public int quality = -1;
        public string bindingOverrides = "";
        public int matches;
        public int wins;
    }

    /// <summary>
    /// Local JSON profile in Application.persistentDataPath. This is also the offline fallback that
    /// the Phase 7 account system builds on (cloud data merges into this file).
    /// </summary>
    public static class SaveSystem
    {
        static SaveData _data;

        static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");

        public static SaveData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _data = null; }

        public static void Load()
        {
            _data = null;
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Could not read profile, starting fresh: " + e.Message);
            }

            if (_data == null) _data = new SaveData();
            if (_data.costume == null) _data.costume = CostumeData.CreateDefault();
            if (string.IsNullOrWhiteSpace(_data.playerName)) _data.playerName = "Player";
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Could not write profile: " + e.Message);
            }
        }
    }
}
