namespace Onix.Scanner.Core.Contracts;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
