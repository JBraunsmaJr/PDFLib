# Wait Strategies

Sometimes your HTML content depends on external resources (like images or fonts) or JavaScript execution.
You can use `WaitStrategy` in `BrowserOptions` to ensure the page is fully loaded before rendering.

## Network Idle

Wait until there are no more than 2 network connections for at least 500ms.

```csharp
var options = new BrowserOptions
{
    WaitStrategy = WaitStrategy.NetworkIdle,
    WaitTimeoutMs = 15000 // Timeout if it takes too long
};
await browser.StartAsync(options);
```

## JavaScript Variable

Wait until a specific JavaScript variable evaluates to a certain value.
This is useful for Single Page Applications (SPAs) or complex JavaScript-driven layouts.

```csharp
var options = new BrowserOptions
{
    WaitStrategy = WaitStrategy.JavascriptVariable,
    WaitVariable = "window.readyToRender",
    WaitVariableValue = "true",
    WaitTimeoutMs = 15000
};
await browser.StartAsync(options);
```

## Timeouts

If timing out is not desirable, you can disable timeouts altogether by setting `WaitTimeoutMs` to null.

```csharp
var options = new BrowserOptions
{
    WaitTimeoutMs = null
};
await browser.StartAsync(options);
```