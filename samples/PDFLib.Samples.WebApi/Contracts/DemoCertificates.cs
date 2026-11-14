using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PDFLib.Samples.WebApi.Contracts;

/// <summary>
/// Self-signed certificates so the sample can sign documents out of the box.
/// </summary>
/// <remarks>
/// These exist only to make the sample runnable. PDF readers will report the signatures as valid but
/// from an unknown issuer. A real service loads certificates issued by a trusted authority from a
/// certificate store, a key vault or an HSM, and the customer's certificate would belong to the customer.
/// </remarks>
public sealed class DemoCertificates : IDisposable
{
    public X509Certificate2 Provider { get; } = Create("Northwind Industrial Supply (demo)");

    public X509Certificate2 Customer { get; } = Create("Customer Signatory (demo)");

    private static X509Certificate2 Create(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
    }

    public void Dispose()
    {
        Provider.Dispose();
        Customer.Dispose();
    }
}
