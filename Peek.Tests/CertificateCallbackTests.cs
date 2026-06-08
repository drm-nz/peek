using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Peek.Tests;

public class CertificateCallbackTests
{
    private static readonly HttpRequestMessage Request = new(HttpMethod.Get, "https://example.com");

    [Fact]
    public void null_cert_with_no_errors_returns_true()
    {
        var result = Program.CertificateCallback(Request, null, null, SslPolicyErrors.None);
        Assert.True(result);
    }

    [Fact]
    public void null_cert_with_errors_returns_false()
    {
        var result = Program.CertificateCallback(Request, null, null, SslPolicyErrors.RemoteCertificateNotAvailable);
        Assert.False(result);
    }

    [Fact]
    public void ssl_name_mismatch_returns_false()
    {
        var result = Program.CertificateCallback(Request, null, null, SslPolicyErrors.RemoteCertificateNameMismatch);
        Assert.False(result);
    }

    [Fact]
    public void ssl_chain_errors_returns_false()
    {
        var result = Program.CertificateCallback(Request, null, null, SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.False(result);
    }

    [Fact]
    public void expiring_cert_logs_warning_and_returns_true()
    {
        var logOutput = new StringWriter();
        Console.SetOut(logOutput);

        using var cert = CreateCert(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));
        var result = Program.CertificateCallback(Request, cert, null, SslPolicyErrors.None);

        Assert.True(result);
        Assert.Contains("expires in", logOutput.ToString());
    }

    [Fact]
    public void valid_cert_without_errors_returns_true()
    {
        using var cert = CreateCert(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1));
        var result = Program.CertificateCallback(Request, cert, null, SslPolicyErrors.None);
        Assert.True(result);
    }

    [Fact]
    public void valid_cert_with_errors_returns_false()
    {
        using var cert = CreateCert(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1));
        var result = Program.CertificateCallback(Request, cert, null, SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.False(result);
    }

    [Fact]
    public void exception_during_cert_inspection_does_not_crash()
    {
        // null reference exception when accessing cert properties should be caught
        var result = Program.CertificateCallback(Request, null, null, SslPolicyErrors.None);
        Assert.True(result);
    }

    private static X509Certificate2 CreateCert(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
