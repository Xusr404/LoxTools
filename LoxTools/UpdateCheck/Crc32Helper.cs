using System;
using System.IO;

namespace LoxTools.UpdateCheck {
    internal static class Crc32Helper {
        private const uint Polynomial = 0xEDB88320u;
        private static readonly uint[] Table = BuildTable();

        public static uint ComputeCrc32(Stream stream) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            }

            uint crc = 0xFFFFFFFFu;
            int b;
            while ((b = stream.ReadByte()) != -1) {
                uint index = (crc ^ (byte)b) & 0xFFu;
                crc = (crc >> 8) ^ Table[index];
            }

            return ~crc;
        }

        private static uint[] BuildTable() {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) {
                uint value = i;
                for (int j = 0; j < 8; j++) {
                    if ((value & 1u) == 1u) {
                        value = (value >> 1) ^ Polynomial;
                    } else {
                        value >>= 1;
                    }
                }
                table[i] = value;
            }
            return table;
        }
    }
}
