using System;
using UnityEngine;

namespace BeefCity.Customization
{
    /// <summary>What a player looks like. Pure data, JSON friendly, safe to send over the network later.</summary>
    [Serializable]
    public class CostumeData
    {
        public Color bodyColor = new Color(0.98f, 0.55f, 0.18f);
        public Color accentColor = Color.white;
        public int hat;
        public int nose;
        public int extra;

        public static readonly string[] Hats = { "None", "Top Hat", "Party Hat", "Cap", "Crown", "Traffic Cone" };
        public static readonly string[] Noses = { "None", "Round", "Long", "Pointy", "Clown" };
        public static readonly string[] Extras = { "None", "Backpack", "Cape", "Antenna" };

        public static readonly Color[] BodyPalette =
        {
            new Color(0.98f, 0.55f, 0.18f), new Color(0.90f, 0.20f, 0.20f), new Color(0.95f, 0.45f, 0.70f),
            new Color(0.55f, 0.30f, 0.85f), new Color(0.20f, 0.45f, 0.95f), new Color(0.20f, 0.80f, 0.90f),
            new Color(0.30f, 0.80f, 0.35f), new Color(0.70f, 0.90f, 0.20f), new Color(0.98f, 0.85f, 0.20f),
            new Color(0.50f, 0.33f, 0.20f), new Color(0.92f, 0.92f, 0.92f), new Color(0.45f, 0.47f, 0.52f)
        };

        public static readonly Color[] AccentPalette =
        {
            Color.white, new Color(0.1f, 0.1f, 0.12f), new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 0.95f),
            new Color(0.3f, 0.8f, 0.35f), new Color(0.98f, 0.85f, 0.2f), new Color(0.95f, 0.45f, 0.7f), new Color(0.98f, 0.55f, 0.18f)
        };

        public static CostumeData CreateDefault() => new CostumeData();

        public static CostumeData CreateRandom(int seed)
        {
            var r = new System.Random(seed * 977 + 13);
            return new CostumeData
            {
                bodyColor = BodyPalette[r.Next(BodyPalette.Length)],
                accentColor = AccentPalette[r.Next(AccentPalette.Length)],
                hat = r.Next(Hats.Length),
                nose = r.Next(Noses.Length),
                extra = r.Next(Extras.Length)
            };
        }

        /// <summary>A distinct look per player slot (used for local guests and remote players).</summary>
        public static CostumeData FromSlot(int slot)
        {
            CostumeData c = CreateRandom(slot + 100);
            c.bodyColor = BodyPalette[slot % BodyPalette.Length];
            return c;
        }

        public CostumeData Clone() => (CostumeData)MemberwiseClone();

        public static int IndexOf(Color[] palette, Color c)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                float d = Mathf.Abs(palette[i].r - c.r) + Mathf.Abs(palette[i].g - c.g) + Mathf.Abs(palette[i].b - c.b);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
