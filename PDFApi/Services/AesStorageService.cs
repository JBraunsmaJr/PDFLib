using System.Security.Cryptography;

namespace PDFApi.Services;

public class AesStorageService : ISecureStorageService
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), "secure_reports");

    public AesStorageService()
    {
        if (!Directory.Exists(_tempPath)) Directory.CreateDirectory(_tempPath);
    }

    public async Task<(string FilePath, byte[] Key, byte[] Iv)> SaveEncryptedAsync(Stream inputStream)
    {
        var fileName = $"{Guid.NewGuid()}.dat";
        var fullPath = Path.Combine(_tempPath, fileName);

        using var aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();

        var key = aes.Key;
        var iv = aes.IV;

        await using var fileStream = new FileStream(fullPath, FileMode.Create);
        await using var cryptoStream = new CryptoStream(fileStream, aes.CreateEncryptor(), CryptoStreamMode.Write);

        await inputStream.CopyToAsync(cryptoStream);
        return (fullPath, key, iv);
    }

    public Stream GetDecryptedStream(string filePath, byte[] key, byte[] iv)
    {
        if (!File.Exists(filePath))
        {
            var tempPath = Path.Combine(_tempPath, filePath);
            if (File.Exists(tempPath)) filePath = tempPath;
            else throw new FileNotFoundException(filePath);
        }

        var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        return new CryptoStream(fileStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
    }

    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        else
        {
            var fullPath = Path.Combine(_tempPath, filePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);    
        }
    }
}