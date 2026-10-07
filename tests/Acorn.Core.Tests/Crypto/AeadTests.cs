using System.Security.Cryptography;
using Acorn.Core.Crypto;

namespace Acorn.Core.Tests.Crypto;

public class AeadTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] Aad = "header"u8.ToArray();
    private static readonly byte[] Plaintext = "vault payload"u8.ToArray();

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var box = Aead.Encrypt(Key, Plaintext, Aad);

        Assert.Equal(Plaintext, Aead.Decrypt(Key, box, Aad));
        Assert.Equal(Aead.NonceSize, box.Nonce.Length);
        Assert.Equal(Aead.TagSize, box.Tag.Length);
    }

    [Fact]
    public void Wrong_key_fails_with_generic_error()
    {
        var box = Aead.Encrypt(Key, Plaintext, Aad);

        var ex = Assert.Throws<VaultAuthenticationException>(() => Aead.Decrypt(RandomNumberGenerator.GetBytes(32), box, Aad));
        Assert.Equal(VaultAuthenticationException.GenericMessage, ex.Message);
    }

    [Fact]
    public void Modified_aad_fails()
    {
        var box = Aead.Encrypt(Key, Plaintext, Aad);

        Assert.Throws<VaultAuthenticationException>(() => Aead.Decrypt(Key, box, "headex"u8));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("ciphertext")]
    [InlineData("tag")]
    public void Every_flipped_bit_fails(string part)
    {
        var original = Aead.Encrypt(Key, Plaintext, Aad);
        var target = part switch
        {
            "nonce" => original.Nonce,
            "ciphertext" => original.Ciphertext,
            _ => original.Tag,
        };

        for (var bit = 0; bit < target.Length * 8; bit++)
        {
            var copy = (byte[])target.Clone();
            copy[bit / 8] ^= (byte)(1 << (bit % 8));
            var tampered = part switch
            {
                "nonce" => original with { Nonce = copy },
                "ciphertext" => original with { Ciphertext = copy },
                _ => original with { Tag = copy },
            };

            Assert.Throws<VaultAuthenticationException>(() => Aead.Decrypt(Key, tampered, Aad));
        }
    }

    [Fact]
    public void Nonces_are_unique_across_1000_encryptions()
    {
        var nonces = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(nonces.Add(Convert.ToHexString(Aead.Encrypt(Key, Plaintext, Aad).Nonce)));
        }
    }

    [Fact]
    public void Rejects_keys_that_are_not_256_bits()
    {
        Assert.Throws<ArgumentException>(() => Aead.Encrypt(new byte[16], Plaintext, Aad));
    }
}
