using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Caraxes
{
    internal static class CanvasFrame
    {
        // Model-specific LED slot indices, in the physical order of the
        // extracted K644CGO-RGB-PRO Cfg.ini [KEY] table. Fn1 is NoColorKey=58.
        static readonly int[] Keys = {
            27,49,50,51,52,53,54,55,56,57,48,189,187,8,
            9,81,87,69,82,84,89,85,73,79,80,219,221,220,
            20,65,83,68,70,71,72,74,75,76,186,222,13,
            160,90,88,67,86,66,78,77,188,190,191,161,
            162,91,164,32,165,250,251,163
        };
        static readonly int[] Slots = {
            1,7,13,19,25,31,37,43,49,55,61,67,73,79,
            2,8,14,20,26,32,38,44,50,56,62,68,74,80,
            3,9,15,21,27,33,39,45,51,57,63,69,81,
            4,10,16,22,28,34,40,46,52,58,64,82,
            5,11,17,35,53,-1,65,83
        };

        internal static byte[] Encode(object value)
        {
            var colors = value as Dictionary<string, object>;
            if (colors == null || colors.Count > Keys.Length) throw new InvalidDataException("Invalid RGB canvas.");
            var frame = Create();
            foreach (var entry in colors)
            {
                int vk;
                int rgb;
                string hex = entry.Value as string;
                if (!int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out vk) ||
                    vk.ToString(CultureInfo.InvariantCulture) != entry.Key || hex == null || hex.Length != 7 || hex[0] != '#' ||
                    !int.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                    throw new InvalidDataException("Invalid RGB pixel.");
                int index = System.Array.IndexOf(Keys, vk);
                if (index < 0) throw new InvalidDataException("Unknown RGB key.");
                if (Slots[index] >= 0) Pixel(frame, Slots[index], rgb);
            }
            return frame;
        }

        internal static byte[] Create()
        {
            var frame = new byte[HidDevice.CanvasLength];
            frame[0] = 8;
            frame[1] = 0x0a;
            frame[2] = 0x7a;
            frame[3] = 1;
            return frame;
        }

        internal static void Pixel(byte[] frame, int slot, int rgb)
        {
            int offset = 4 + slot * 3;
            frame[offset] = (byte)(rgb >> 16);
            frame[offset + 1] = (byte)(rgb >> 8);
            frame[offset + 2] = (byte)rgb;
        }

        internal static byte[] Render(System.Func<int, int, int> color)
        {
            var frame = Create();
            foreach (int slot in Slots)
                if (slot >= 0) Pixel(frame, slot, color(slot / 6, slot % 6 - 1));
            return frame;
        }
    }
}
