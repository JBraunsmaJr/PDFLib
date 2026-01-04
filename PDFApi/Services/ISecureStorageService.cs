namespace PDFApi.Services;

public interface ISecureStorageService
{
    Task<(string FilePath, byte[] Key, byte[] Iv)> SaveEncryptedAsync(Stream inputStream);
    Stream GetDecryptedStream(string filePath, byte[] key, byte[] iv);
    void DeleteFile(string filePath);
}