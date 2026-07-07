using ModelContextProtocol.AspNetCore;

namespace Delibera.Server.Mcp;

/// <summary>
///    Maps the MCP HTTP endpoint so any compliant MCP client
///    (Claude Desktop, Cursor, custom agents) can connect and discover
///    Delibera tools via the Model Context Protocol.
/// </summary>
public static class McpEndpointExtensions
{
    /// <summary>
    ///    Registers <c>GET /mcp</c> (SSE handshake) and <c>POST /mcp</c>
    ///    (JSON-RPC messages) on the <paramref name="app" />.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication" /> to map the endpoint on.</param>
    /// <param name="pattern">Route pattern (default: <c>"/mcp"</c>).</param>
    public static WebApplication MapDeliberaMcp(
        this WebApplication app,
        string pattern = "/mcp")
    {
        app.MapMcp(pattern);
        return app;
    }
}
