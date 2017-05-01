using System;
using System.Collections.Generic;
using Shadowsocks.Encryption.AEAD;
using Shadowsocks.Encryption.Stream;

namespace Shadowsocks.Encryption
{
    public static class EncryptorFactory
    {
        private static readonly Dictionary<string, Type> _registeredEncryptors = new Dictionary<string, Type>();

        private static readonly Type[] ConstructorTypes = {typeof(string), typeof(string)};

        static EncryptorFactory()
        {
            foreach (var method in StreamMbedTLSEncryptor.SupportedCiphers())
                _registeredEncryptors.Add(method, typeof(StreamMbedTLSEncryptor));
            foreach (var method in StreamSodiumEncryptor.SupportedCiphers())
                _registeredEncryptors.Add(method, typeof(StreamSodiumEncryptor));
            foreach (var method in AEADMbedTLSEncryptor.SupportedCiphers())
                _registeredEncryptors.Add(method, typeof(AEADMbedTLSEncryptor));
            foreach (var method in AEADSodiumEncryptor.SupportedCiphers())
                _registeredEncryptors.Add(method, typeof(AEADSodiumEncryptor));
        }

        public static IEncryptor GetEncryptor(string method, string password)
        {
            if (method.IsNullOrEmpty())
                method = "aes-256-cfb";
            method = method.ToLowerInvariant();
            var t = _registeredEncryptors[method];
            var c = t.GetConstructor(ConstructorTypes);
            if (c == null) throw new System.Exception("Invalid ctor");
            var result = (IEncryptor) c.Invoke(new object[] {method, password});
            return result;
        }
    }
}