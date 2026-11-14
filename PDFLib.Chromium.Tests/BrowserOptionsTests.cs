namespace PDFLib.Chromium.Tests;

public class BrowserOptionsTests
{
    [Fact]
    public void WaitTimeout_DefaultsToTenSeconds()
    {
        // Regression: this used TimeSpan.Milliseconds (the 0-999 component), which evaluates to 0
        Assert.Equal(10_000, new BrowserOptions().WaitTimeoutMs);
    }
}
