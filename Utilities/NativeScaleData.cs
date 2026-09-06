using System;

namespace ScalerCore.Utilities
{
    // What a natively scaled respawn carries in its Photon instantiation data.
    //
    // The game's own PhysGrabObject.Awake reads the first three entries as the object's scale on
    // every machine, modded or not: that is the whole reason a respawn is visible to players
    // without ScalerCore. Everything after those three is ours. A marker string says the data is
    // ScalerCore's, then the original scale (the transform is already scaled by the time our
    // controller wakes up, so it cannot be read back), the factor the clone should animate in
    // from, the seconds left on a timed session, and the session options in the same two arrays
    // the shrink RPC has always used. Vanilla never looks past index 2.
    internal static class NativeScaleData
    {
        public const string Marker = "ScalerCore.native.1";
        const int Count = 11;

        public static object[] Pack(float[] scaled, float[] original, float fromFactor, float remaining, float[] opts, bool[] flags)
        {
            if (scaled.Length != 3 || original.Length != 3) throw new ArgumentException("scale triplets");
            return new object[]
            {
                scaled[0], scaled[1], scaled[2],
                Marker,
                original[0], original[1], original[2],
                fromFactor,
                remaining,
                opts,
                flags
            };
        }

        public static bool IsOurs(object[]? data) => data != null && data.Length >= Count && data[3] is string s && s == Marker;

        public static bool TryUnpack(object[]? data, out float[] original, out float fromFactor, out float remaining, out float[] opts, out bool[] flags)
        {
            original = new float[3];
            fromFactor = 0f;
            remaining = 0f;
            opts = Array.Empty<float>();
            flags = Array.Empty<bool>();
            if (!IsOurs(data)) return false;
            try
            {
                original[0] = Convert.ToSingle(data![4]);
                original[1] = Convert.ToSingle(data[5]);
                original[2] = Convert.ToSingle(data[6]);
                fromFactor = Convert.ToSingle(data[7]);
                remaining = Convert.ToSingle(data[8]);
                opts = data[9] as float[] ?? Array.Empty<float>();
                flags = data[10] as bool[] ?? Array.Empty<bool>();
                return original[0] > 0f && original[1] > 0f && original[2] > 0f;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
