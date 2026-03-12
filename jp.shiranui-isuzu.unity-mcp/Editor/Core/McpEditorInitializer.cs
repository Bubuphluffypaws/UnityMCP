using System;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Resources;
using UnityMCP.Editor.Settings;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Handles initialization of the MCP system when the Unity editor starts.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpEditorInitializer
    {
        /// <summary>
        /// Initializes the MCP system when the Unity editor starts.
        /// Also hooks into assembly reload to cleanly dispose the old server
        /// before domain reload destroys all static state.
        /// </summary>
        static McpEditorInitializer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.delayCall += Initialize;
        }

        /// <summary>
        /// Disposes the existing MCP server before domain reload wipes static state.
        /// Without this, the old TcpClient/threads leak since finalizers aren't guaranteed.
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            try
            {
                // Unregister play mode handler to prevent accumulation across reloads
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

                if (McpServiceManager.Instance.TryGetService<McpServer>(out var server))
                {
                    server.Dispose();
                    McpServiceManager.Instance.RemoveService<McpServer>();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[McpEditorInitializer] Error during pre-reload cleanup: {e.Message}");
            }
        }

        /// <summary>
        /// Initializes the MCP system, registering services and starting the server if configured.
        /// </summary>
        private static void Initialize()
        {
            Debug.Log("Initializing Unity MCP system...");

            // Dispose any leftover server from a previous domain (belt-and-suspenders)
            if (McpServiceManager.Instance.TryGetService<McpServer>(out var oldServer))
            {
                Debug.Log("[McpEditorInitializer] Disposing leftover MCP server from previous domain");
                oldServer.Dispose();
                McpServiceManager.Instance.RemoveService<McpServer>();
            }

            // Create and register the MCP server
            var settings = McpSettings.instance;
            var server = new McpServer();
            McpServiceManager.Instance.RegisterService(server);

            // Discover and register command handlers
            var commandDiscovery = new McpHandlerDiscovery<IMcpCommandHandler>(handler => server.RegisterHandler(handler));
            var commandCount = commandDiscovery.DiscoverAndRegister();

            if (settings.detailedLogs)
                Debug.Log($"Discovered and registered {commandCount} command handlers");

            // Discover and register resource handlers
            var resourceDiscovery = new McpHandlerDiscovery<IMcpResourceHandler>(handler => server.RegisterResourceHandler(handler));
            var resourceCount = resourceDiscovery.DiscoverAndRegister();
            if (settings.detailedLogs)
                Debug.Log($"Discovered and registered {resourceCount} resource handlers");

            // Auto-start if configured
            if (settings.autoStartOnLaunch)
            {
                server.Start();
            }

            // Register for play mode state change if auto-restart is enabled
            if (settings.autoRestartOnPlayModeChange)
            {
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            }

            Debug.Log("Unity MCP system initialized");
        }

        /// <summary>
        /// Handles play mode state changes, restarting the server if needed.
        /// </summary>
        /// <param name="state">The new play mode state.</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!McpServiceManager.Instance.TryGetService<McpServer>(out var server))
            {
                return;
            }

            // When entering or exiting play mode
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                if (server.IsRunning)
                {
                    Debug.Log("Restarting MCP server due to play mode change");
                    server.Stop();
                    server.Start();
                }
            }
        }
    }
}
