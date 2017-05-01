using System;
using System.Collections.Generic;
using ShadowSocksProxy.Encryption.Exception;

namespace ShadowSocksProxy.Encryption.Stream
{
    public class StreamSodiumEncryptor
        : StreamEncryptor, IDisposable
    {
        private const int CIPHER_SALSA20 = 1;
        private const int CIPHER_CHACHA20 = 2;
        private const int CIPHER_CHACHA20_IETF = 3;

        private const int SODIUM_BLOCK_SIZE = 64;

        private static readonly Dictionary<string, EncryptorInfo> _ciphers = new Dictionary<string, EncryptorInfo>
        {
            {"salsa20", new EncryptorInfo(32, 8, CIPHER_SALSA20)},
            {"chacha20", new EncryptorInfo(32, 8, CIPHER_CHACHA20)},
            {"chacha20-ietf", new EncryptorInfo(32, 12, CIPHER_CHACHA20_IETF)}
        };

        protected byte[] _decryptBuf;
        protected int _decryptBytesRemaining;
        protected ulong _decryptIC;
        protected byte[] _encryptBuf;

        protected int _encryptBytesRemaining;
        protected ulong _encryptIC;

        public StreamSodiumEncryptor(string method, string password)
            : base(method, password)
        {
            _encryptBuf = new byte[MAX_INPUT_SIZE + SODIUM_BLOCK_SIZE];
            _decryptBuf = new byte[MAX_INPUT_SIZE + SODIUM_BLOCK_SIZE];
        }

        public override void Dispose()
        {
        }

        protected override Dictionary<string, EncryptorInfo> getCiphers()
        {
            return _ciphers;
        }

        public static List<string> SupportedCiphers()
        {
            return new List<string>(_ciphers.Keys);
        }

        protected override void cipherUpdate(bool isEncrypt, int length, byte[] buf, byte[] outbuf)
        {
            // TODO write a unidirection cipher so we don't have to if if if
            int bytesRemaining;
            ulong ic;
            byte[] sodiumBuf;
            byte[] iv;
            var ret = -1;

            if (isEncrypt)
            {
                bytesRemaining = _encryptBytesRemaining;
                ic = _encryptIC;
                sodiumBuf = _encryptBuf;
                iv = _encryptIV;
            }
            else
            {
                bytesRemaining = _decryptBytesRemaining;
                ic = _decryptIC;
                sodiumBuf = _decryptBuf;
                iv = _decryptIV;
            }
            var padding = bytesRemaining;
            Buffer.BlockCopy(buf, 0, sodiumBuf, padding, length);

            switch (_cipher)
            {
                case CIPHER_SALSA20:
                    ret = Sodium.crypto_stream_salsa20_xor_ic(sodiumBuf, sodiumBuf, (ulong) (padding + length), iv, ic,
                        _key);
                    break;
                case CIPHER_CHACHA20:
                    ret = Sodium.crypto_stream_chacha20_xor_ic(sodiumBuf, sodiumBuf, (ulong) (padding + length), iv, ic,
                        _key);
                    break;
                case CIPHER_CHACHA20_IETF:
                    ret = Sodium.crypto_stream_chacha20_ietf_xor_ic(sodiumBuf, sodiumBuf, (ulong) (padding + length), iv,
                        (uint) ic, _key);
                    break;
            }
            if (ret != 0) throw new CryptoErrorException();

            Buffer.BlockCopy(sodiumBuf, padding, outbuf, 0, length);
            padding += length;
            ic += (ulong) padding/SODIUM_BLOCK_SIZE;
            bytesRemaining = padding%SODIUM_BLOCK_SIZE;

            if (isEncrypt)
            {
                _encryptBytesRemaining = bytesRemaining;
                _encryptIC = ic;
            }
            else
            {
                _decryptBytesRemaining = bytesRemaining;
                _decryptIC = ic;
            }
        }
    }
}