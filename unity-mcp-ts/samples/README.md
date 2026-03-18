# Optional Handlers

These handlers are **not enabled by default** for security reasons.

## CodeExecutionCommandHandler

Allows executing arbitrary C# code in the Unity editor via MCP. This is powerful for rapid prototyping but is a **security risk** — any MCP client can execute arbitrary code with full Unity API access.

### To enable:

1. Copy `CodeExecutionCommandHandler.ts.sample` to `src/handlers/CodeExecutionCommandHandler.ts`
2. Copy the C# handler from `jp.shiranui-isuzu.unity-mcp/Samples~/UnityMCPHandlerSamples/Editor/CodeExecutionCommandHandler.cs` into the Unity package's `Editor/Handlers/` directory
3. Rebuild the TS bridge (`npm run build`)
4. Restart the MCP bridge and Unity

### To disable:

Remove or rename the files from step 1-2 above.
