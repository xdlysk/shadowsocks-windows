﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using Cyotek.Collections.Generic;
using ShadowSocksProxy.Controller;
using ShadowSocksProxy.Controller.Service;
using ShadowSocksProxy.Encryption.Exception;
using ShadowSocksProxy.Encryption.Stream;

namespace ShadowSocksProxy.Encryption.AEAD
{
    public abstract class AEADEncryptor
        : EncryptorBase
    {
        // We are using the same saltLen and keyLen
        private const string Info = "ss-subkey";

        public const int CHUNK_LEN_BYTES = 2;
        public const uint CHUNK_LEN_MASK = 0x3FFFu;
        private static readonly byte[] InfoBytes = Encoding.ASCII.GetBytes(Info);

        // for UDP only
        protected static byte[] _udpTmpBuf = new byte[65536];
        protected static byte[] _Masterkey;
        protected int _cipher;
        private readonly CircularBuffer<byte> _decCircularBuffer = new CircularBuffer<byte>(MAX_INPUT_SIZE*2, false);
        protected byte[] _decNonce;
        protected byte[] _decryptSalt;
        // Is first packet
        protected bool _decryptSaltReceived;

        // every connection should create its own buffer
        private readonly CircularBuffer<byte> _encCircularBuffer = new CircularBuffer<byte>(MAX_INPUT_SIZE*2, false);
        protected byte[] _encNonce;

        protected byte[] _encryptSalt;
        protected bool _encryptSaltSent;
        // internal name in the crypto library
        protected string _innerLibName;

        protected string _method;

        protected object _nonceIncrementLock = new object();
        protected byte[] _sessionKey;

        // Is first chunk(tcp request)
        protected bool _tcpRequestSent;
        protected EncryptorInfo CipherInfo;

        protected Dictionary<string, EncryptorInfo> ciphers;
        protected int keyLen;
        protected int nonceLen;
        protected int saltLen;
        protected int tagLen;

        public AEADEncryptor(string method, string password)
            : base(method, password)
        {
            InitEncryptorInfo(method);
            InitKey(password);
            // Initialize all-zero nonce for each connection
            _encNonce = new byte[nonceLen];
            _decNonce = new byte[nonceLen];
        }

        protected abstract Dictionary<string, EncryptorInfo> getCiphers();

        protected void InitEncryptorInfo(string method)
        {
            method = method.ToLower();
            _method = method;
            ciphers = getCiphers();
            CipherInfo = ciphers[_method];
            _innerLibName = CipherInfo.InnerLibName;
            _cipher = CipherInfo.Type;
            if (_cipher == 0) throw new System.Exception("method not found");
            keyLen = CipherInfo.KeySize;
            saltLen = CipherInfo.SaltSize;
            tagLen = CipherInfo.TagSize;
            nonceLen = CipherInfo.NonceSize;
        }

        protected void InitKey(string password)
        {
            var passbuf = Encoding.UTF8.GetBytes(password);
            // init master key
            if (_Masterkey == null) _Masterkey = new byte[keyLen];
            if (_Masterkey.Length != keyLen) Array.Resize(ref _Masterkey, keyLen);
            DeriveKey(passbuf, _Masterkey, keyLen);
            // init session key
            if (_sessionKey == null) _sessionKey = new byte[keyLen];
        }

        public void DeriveKey(byte[] password, byte[] key, int keylen)
        {
            StreamEncryptor.LegacyDeriveKey(password, key, keylen);
        }

        public void DeriveSessionKey(byte[] salt, byte[] masterKey, byte[] sessionKey)
        {
            var ret = MbedTLS.hkdf(salt, saltLen, masterKey, keyLen, InfoBytes, InfoBytes.Length, sessionKey,
                keyLen);
            if (ret != 0) throw new System.Exception("failed to generate session key");
        }

        protected void IncrementNonce(bool isEncrypt)
        {
            lock (_nonceIncrementLock)
            {
                Sodium.sodium_increment(isEncrypt ? _encNonce : _decNonce, nonceLen);
            }
        }

        public virtual void InitCipher(byte[] salt, bool isEncrypt, bool isUdp)
        {
            if (isEncrypt)
            {
                _encryptSalt = new byte[saltLen];
                Array.Copy(salt, _encryptSalt, saltLen);
            }
            else
            {
                _decryptSalt = new byte[saltLen];
                Array.Copy(salt, _decryptSalt, saltLen);
            }
            Logging.Dump("Salt", salt, saltLen);
        }

        public static void randBytes(byte[] buf, int length)
        {
            RNG.GetBytes(buf, length);
        }

        public abstract int cipherEncrypt(byte[] plaintext, uint plen, byte[] ciphertext, ref uint clen);

        public abstract int cipherDecrypt(byte[] ciphertext, uint clen, byte[] plaintext, ref uint plen);

        // we know the plaintext length before encryption, so we can do it in one operation
        private void ChunkEncrypt(byte[] plaintext, int plainLen, byte[] ciphertext, out int cipherLen)
        {
            if (plainLen > CHUNK_LEN_MASK)
            {
                Logging.Error("enc chunk too big");
                throw new CryptoErrorException();
            }

            // encrypt len
            var encLenBytes = new byte[CHUNK_LEN_BYTES + tagLen];
            uint encChunkLenLength = 0;
            var lenbuf = BitConverter.GetBytes((ushort) IPAddress.HostToNetworkOrder((short) plainLen));
            cipherEncrypt(lenbuf, CHUNK_LEN_BYTES, encLenBytes, ref encChunkLenLength);
            Debug.Assert(encChunkLenLength == CHUNK_LEN_BYTES + tagLen);
            IncrementNonce(true);

            // encrypt corresponding data
            var encBytes = new byte[plainLen + tagLen];
            uint encBufLength = 0;
            cipherEncrypt(plaintext, (uint) plainLen, encBytes, ref encBufLength);
            Debug.Assert(encBufLength == plainLen + tagLen);
            IncrementNonce(true);

            // construct outbuf
            Array.Copy(encLenBytes, 0, ciphertext, 0, (int) encChunkLenLength);
            Buffer.BlockCopy(encBytes, 0, ciphertext, (int) encChunkLenLength, (int) encBufLength);
            cipherLen = (int) (encChunkLenLength + encBufLength);
        }

        #region TCP

        public override void Encrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            Debug.Assert(_encCircularBuffer != null, "_encCircularBuffer != null");

            _encCircularBuffer.Put(buf, 0, length);
            outlength = 0;
            Logging.Debug("---Start Encryption");
            if (!_encryptSaltSent)
            {
                _encryptSaltSent = true;
                // Generate salt
                var saltBytes = new byte[saltLen];
                randBytes(saltBytes, saltLen);
                InitCipher(saltBytes, true, false);
                Array.Copy(saltBytes, 0, outbuf, 0, saltLen);
                outlength = saltLen;
                Logging.Debug($"_encryptSaltSent outlength {outlength}");
            }

            if (!_tcpRequestSent)
            {
                _tcpRequestSent = true;
                // The first TCP request
                int encAddrBufLength;
                var encAddrBufBytes = new byte[AddrBufLength + tagLen*2 + CHUNK_LEN_BYTES];
                var addrBytes = _encCircularBuffer.Get(AddrBufLength);
                ChunkEncrypt(addrBytes, AddrBufLength, encAddrBufBytes, out encAddrBufLength);
                Debug.Assert(encAddrBufLength == AddrBufLength + tagLen*2 + CHUNK_LEN_BYTES);
                Array.Copy(encAddrBufBytes, 0, outbuf, outlength, encAddrBufLength);
                outlength += encAddrBufLength;
                Logging.Debug($"_tcpRequestSent outlength {outlength}");
            }

            // handle other chunks
            while (true)
            {
                var bufSize = (uint) _encCircularBuffer.Size;
                if (bufSize <= 0) return;
                var chunklength = (int) Math.Min(bufSize, CHUNK_LEN_MASK);
                var chunkBytes = _encCircularBuffer.Get(chunklength);
                int encChunkLength;
                var encChunkBytes = new byte[chunklength + tagLen*2 + CHUNK_LEN_BYTES];
                ChunkEncrypt(chunkBytes, chunklength, encChunkBytes, out encChunkLength);
                Debug.Assert(encChunkLength == chunklength + tagLen*2 + CHUNK_LEN_BYTES);
                Buffer.BlockCopy(encChunkBytes, 0, outbuf, outlength, encChunkLength);
                outlength += encChunkLength;
                Logging.Debug("chunks enc outlength " + outlength);
                // check if we have enough space for outbuf
                if (outlength + TCPHandler.ChunkOverheadSize > TCPHandler.BufferSize)
                {
                    Logging.Debug("enc outbuf almost full, giving up");
                    return;
                }
                bufSize = (uint) _encCircularBuffer.Size;
                if (bufSize <= 0)
                {
                    Logging.Debug("No more data to encrypt, leaving");
                    return;
                }
            }
        }


        public override void Decrypt(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            Debug.Assert(_decCircularBuffer != null, "_decCircularBuffer != null");
            int bufSize;
            outlength = 0;
            // drop all into buffer
            _decCircularBuffer.Put(buf, 0, length);

            Logging.Debug("---Start Decryption");
            if (!_decryptSaltReceived)
            {
                bufSize = _decCircularBuffer.Size;
                // check if we get the leading salt
                if (bufSize <= saltLen) return;
                _decryptSaltReceived = true;
                var salt = _decCircularBuffer.Get(saltLen);
                InitCipher(salt, false, false);
                Logging.Debug("get salt len " + saltLen);
            }

            // handle chunks
            while (true)
            {
                bufSize = _decCircularBuffer.Size;
                // check if we have any data
                if (bufSize <= 0)
                {
                    Logging.Debug("No data in _decCircularBuffer");
                    return;
                }

                // first get chunk length
                if (bufSize <= CHUNK_LEN_BYTES + tagLen) return;

                #region Chunk Decryption

                var encLenBytes = _decCircularBuffer.Peek(CHUNK_LEN_BYTES + tagLen);
                uint decChunkLenLength = 0;
                var decChunkLenBytes = new byte[CHUNK_LEN_BYTES];
                // try to dec chunk len
                cipherDecrypt(encLenBytes, CHUNK_LEN_BYTES + (uint) tagLen, decChunkLenBytes, ref decChunkLenLength);
                Debug.Assert(decChunkLenLength == CHUNK_LEN_BYTES);
                // finally we get the real chunk len
                var chunkLen = (ushort) IPAddress.NetworkToHostOrder((short) BitConverter.ToUInt16(decChunkLenBytes, 0));
                if (chunkLen > CHUNK_LEN_MASK)
                {
                    // we get invalid chunk
                    Logging.Error($"Invalid chunk length: {chunkLen}");
                    throw new CryptoErrorException();
                }
                Logging.Debug("Get the real chunk len:" + chunkLen);
                bufSize = _decCircularBuffer.Size;
                if (bufSize < CHUNK_LEN_BYTES + tagLen /* we haven't remove them */+ chunkLen + tagLen)
                {
                    Logging.Debug("No more data to decrypt one chunk");
                    return;
                }
                IncrementNonce(false);

                // we have enough data to decrypt one chunk
                // drop chunk len and its tag from buffer
                _decCircularBuffer.Get(CHUNK_LEN_BYTES + tagLen);
                var encChunkBytes = _decCircularBuffer.Get(chunkLen + tagLen);
                var decChunkBytes = new byte[chunkLen];
                uint decChunkLen = 0;
                cipherDecrypt(encChunkBytes, chunkLen + (uint) tagLen, decChunkBytes, ref decChunkLen);
                Debug.Assert(decChunkLen == chunkLen);
                IncrementNonce(false);

                #endregion

                // output to outbuf
                Buffer.BlockCopy(decChunkBytes, 0, outbuf, outlength, (int) decChunkLen);
                outlength += (int) decChunkLen;
                Logging.Debug("aead dec outlength " + outlength);
                if (outlength + 100 > TCPHandler.BufferSize)
                {
                    Logging.Debug("dec outbuf almost full, giving up");
                    return;
                }
                bufSize = _decCircularBuffer.Size;
                // check if we already done all of them
                if (bufSize <= 0)
                {
                    Logging.Debug("No data in _decCircularBuffer, already all done");
                    return;
                }
            }
        }

        #endregion

        #region UDP

        public override void EncryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            // Generate salt
            randBytes(outbuf, saltLen);
            InitCipher(outbuf, true, true);
            uint olen = 0;
            lock (_udpTmpBuf)
            {
                cipherEncrypt(buf, (uint) length, _udpTmpBuf, ref olen);
                Debug.Assert(olen == length + tagLen);
                Buffer.BlockCopy(_udpTmpBuf, 0, outbuf, saltLen, (int) olen);
                outlength = (int) (saltLen + olen);
            }
        }

        public override void DecryptUDP(byte[] buf, int length, byte[] outbuf, out int outlength)
        {
            InitCipher(buf, false, true);
            uint olen = 0;
            lock (_udpTmpBuf)
            {
                // copy remaining data to first pos
                Buffer.BlockCopy(buf, saltLen, buf, 0, length - saltLen);
                cipherDecrypt(buf, (uint) (length - saltLen), _udpTmpBuf, ref olen);
                Buffer.BlockCopy(_udpTmpBuf, 0, outbuf, 0, (int) olen);
                outlength = (int) olen;
            }
        }

        #endregion
    }
}