﻿namespace ShadowSocksProxy.Encryption
{
    public class EncryptorInfo
    {
        public string InnerLibName;
        public int IvSize;
        public int KeySize;
        public int NonceSize;
        public int SaltSize;
        public int TagSize;
        public int Type;

        // For those who make use of internal crypto method name
        // e.g. mbed TLS

        #region Stream ciphers

        public EncryptorInfo(string innerLibName, int keySize, int ivSize, int type)
        {
            KeySize = keySize;
            IvSize = ivSize;
            Type = type;
            InnerLibName = innerLibName;
        }

        public EncryptorInfo(int keySize, int ivSize, int type)
        {
            KeySize = keySize;
            IvSize = ivSize;
            Type = type;
            InnerLibName = string.Empty;
        }

        #endregion

        #region AEAD ciphers

        public EncryptorInfo(string innerLibName, int keySize, int saltSize, int nonceSize, int tagSize, int type)
        {
            KeySize = keySize;
            SaltSize = saltSize;
            NonceSize = nonceSize;
            TagSize = tagSize;
            Type = type;
            InnerLibName = innerLibName;
        }

        public EncryptorInfo(int keySize, int saltSize, int nonceSize, int tagSize, int type)
        {
            KeySize = keySize;
            SaltSize = saltSize;
            NonceSize = nonceSize;
            TagSize = tagSize;
            Type = type;
            InnerLibName = string.Empty;
        }

        #endregion
    }

    public abstract class EncryptorBase
        : IEncryptor
    {
        public const int MAX_INPUT_SIZE = 32768;

        public const int MAX_DOMAIN_LEN = 255;
        public const int ADDR_PORT_LEN = 2;
        public const int ADDR_ATYP_LEN = 1;

        public const int ATYP_IPv4 = 0x01;
        public const int ATYP_DOMAIN = 0x03;
        public const int ATYP_IPv6 = 0x04;

        public const int MD5_LEN = 16;

        protected string Method;
        protected string Password;

        protected EncryptorBase(string method, string password)
        {
            Method = method;
            Password = password;
        }

        public abstract void Encrypt(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void Decrypt(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void EncryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void DecryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void Dispose();

        public int AddrBufLength { get; set; } = -1;
    }
}