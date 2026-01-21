using System;
using System.IO;

namespace EQEmu_Patcher
{
    /// <summary>
    /// xxHash64 implementation to match the Node.js xxhash-wasm library used in nexus.js
    /// </summary>
    public static class XXHash64
    {
        private const ulong PRIME64_1 = 11400714785074694791UL;
        private const ulong PRIME64_2 = 14029467366897019727UL;
        private const ulong PRIME64_3 = 1609587929392839161UL;
        private const ulong PRIME64_4 = 9650029242287828579UL;
        private const ulong PRIME64_5 = 2870177450012600261UL;

        public static ulong ComputeHash(byte[] data, ulong seed = 0)
        {
            ulong h64;
            int index = 0;
            int len = data.Length;

            if (len >= 32)
            {
                int limit = len - 32;
                ulong v1 = seed + PRIME64_1 + PRIME64_2;
                ulong v2 = seed + PRIME64_2;
                ulong v3 = seed + 0;
                ulong v4 = seed - PRIME64_1;

                do
                {
                    v1 = Round(v1, BitConverter.ToUInt64(data, index));
                    index += 8;
                    v2 = Round(v2, BitConverter.ToUInt64(data, index));
                    index += 8;
                    v3 = Round(v3, BitConverter.ToUInt64(data, index));
                    index += 8;
                    v4 = Round(v4, BitConverter.ToUInt64(data, index));
                    index += 8;
                } while (index <= limit);

                h64 = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                h64 = MergeRound(h64, v1);
                h64 = MergeRound(h64, v2);
                h64 = MergeRound(h64, v3);
                h64 = MergeRound(h64, v4);
            }
            else
            {
                h64 = seed + PRIME64_5;
            }

            h64 += (ulong)len;

            // Process remaining bytes in 8-byte chunks
            while (index + 8 <= len)
            {
                ulong k1 = Round(0, BitConverter.ToUInt64(data, index));
                index += 8;
                h64 ^= k1;
                h64 = RotateLeft(h64, 27) * PRIME64_1 + PRIME64_4;
            }

            // Process remaining bytes in 4-byte chunks
            while (index + 4 <= len)
            {
                h64 ^= BitConverter.ToUInt32(data, index) * PRIME64_1;
                index += 4;
                h64 = RotateLeft(h64, 23) * PRIME64_2 + PRIME64_3;
            }

            // Process remaining bytes
            while (index < len)
            {
                h64 ^= data[index] * PRIME64_5;
                index++;
                h64 = RotateLeft(h64, 11) * PRIME64_1;
            }

            // Final mix
            h64 ^= h64 >> 33;
            h64 *= PRIME64_2;
            h64 ^= h64 >> 29;
            h64 *= PRIME64_3;
            h64 ^= h64 >> 32;

            return h64;
        }

        public static string ComputeHashString(byte[] data, ulong seed = 0)
        {
            ulong hash = ComputeHash(data, seed);
            return hash.ToString("X16");
        }

        public static string ComputeFileHash(string filePath)
        {
            byte[] data = File.ReadAllBytes(filePath);
            return ComputeHashString(data);
        }

        private static ulong Round(ulong acc, ulong input)
        {
            acc += input * PRIME64_2;
            acc = RotateLeft(acc, 31);
            acc *= PRIME64_1;
            return acc;
        }

        private static ulong MergeRound(ulong acc, ulong val)
        {
            val = Round(0, val);
            acc ^= val;
            acc = acc * PRIME64_1 + PRIME64_4;
            return acc;
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
    }
}
