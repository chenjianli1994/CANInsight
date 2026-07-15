using System;
using System.Security.Cryptography;
using System.Linq;

namespace PCAN_Client.Update
{
    internal class Signer
    {
        private const int BlockSize = 16; // AES block size is 16 bytes

        public static byte[] SignData(byte[] data, byte[] privateKeyBytes)
        {
            // 1. 计算数据的SHA256哈希
            byte[] dataHash;
            byte[] result = new byte[BaseParamter.KeyLength];
            using (SHA256 sha256 = SHA256.Create())
            {
                dataHash = sha256.ComputeHash(data);
            }
            var temp = ComputeCMAC(dataHash, privateKeyBytes);
            Array.Copy(temp, 0, result, 0, temp.Length);
            Array.Copy(Enumerable.Repeat(BaseParamter.KeyFillValue, BaseParamter.KeyLength - temp.Length).ToArray(), 0, result, temp.Length, BaseParamter.KeyLength - temp.Length);

            return result;
        }

        public static byte[] ComputeCMAC(byte[] data, byte[] key)
        {
            // 验证输入参数
            if (key == null || key.Length != 16)
                throw new ArgumentException("Key must be 16 bytes for AES-128");

            if (data == null)
                data = Array.Empty<byte>();

            // 1. 生成子密钥 K1 和 K2
            (byte[] k1, byte[] k2) = GenerateSubkeys(key);

            // 2. 计算分组数
            int fullBlockCount = data.Length / BlockSize;
            bool hasFullBlocks = fullBlockCount > 0;
            bool needsPadding = (data.Length % BlockSize) != 0;

            // 3. 处理完整块
            byte[] iv = new byte[BlockSize]; // 初始IV（全零）
            if (hasFullBlocks)
            {
                for (int i = 0; i < fullBlockCount - 1; i++)
                {
                    byte[] block = new byte[BlockSize];
                    Buffer.BlockCopy(data, i * BlockSize, block, 0, BlockSize);
                    iv = ProcessBlock(block, key, iv);
                }
            }

            // 4. 处理最后一个块
            byte[] lastBlock = new byte[BlockSize];
            int bytesInLastBlock = data.Length - (fullBlockCount * BlockSize);

            if (needsPadding || data.Length == 0)
            {
                // 需要填充：复制已有数据后添加0x80和0x00
                if (bytesInLastBlock > 0)
                    Buffer.BlockCopy(data, data.Length - bytesInLastBlock, lastBlock, 0, bytesInLastBlock);

                lastBlock[bytesInLastBlock] = 0x80; // 填充起始标记

                // 与K2异或
                lastBlock = XorBytes(lastBlock, k2);
            }
            else
            {
                // 完整块：直接复制并与K1异或
                Buffer.BlockCopy(data, data.Length - BlockSize, lastBlock, 0, BlockSize);
                lastBlock = XorBytes(lastBlock, k1);
            }

            // 5. 处理最后一个块并返回结果
            byte[] result = ProcessBlock(lastBlock, key, iv);
            return result;
        }

        // 生成子密钥 K1 和 K2
        private static (byte[] k1, byte[] k2) GenerateSubkeys(byte[] key)
        {
            // 加密全零块获取L
            byte[] L = EncryptBlock(new byte[BlockSize], key);

            // 生成K1
            byte[] k1 = ShiftLeft(L);
            if ((L[0] & 0x80) != 0) // 检查最高位
                k1[BlockSize - 1] ^= 0x87;

            // 生成K2
            byte[] k2 = ShiftLeft(k1);
            if ((k1[0] & 0x80) != 0)
                k2[BlockSize - 1] ^= 0x87;

            return (k1, k2);
        }

        // 左移操作（整个数组）
        private static byte[] ShiftLeft(byte[] bytes)
        {
            byte[] result = new byte[bytes.Length];
            byte carry = 0;

            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                ushort val = (ushort)(bytes[i] << 1);
                result[i] = (byte)((val & 0xFF) | carry);
                carry = (byte)((val & 0xFF00) >> 8);
            }

            return result;
        }

        // 处理一个数据块
        private static byte[] ProcessBlock(byte[] input, byte[] key, byte[] iv)
        {
            // 输入块与IV异或
            byte[] xored = XorBytes(input, iv);

            // 加密异或结果
            return EncryptBlock(xored, key);
        }

        // 字节数组异或操作
        private static byte[] XorBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Arrays must be same length");

            byte[] result = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
                result[i] = (byte)(a[i] ^ b[i]);

            return result;
        }

        // AES加密单个数据块
        private static byte[] EncryptBlock(byte[] input, byte[] key)
        {
            var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            var encryptor = aes.CreateEncryptor();
            byte[] output = new byte[BlockSize];
            encryptor.TransformBlock(input, 0, BlockSize, output, 0);
            return output;
        }

        public static byte[] GenerateSecureRandomBytes(int len)
        {
            byte[] randomBytes = new byte[len];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            return randomBytes;
        }
    }
}