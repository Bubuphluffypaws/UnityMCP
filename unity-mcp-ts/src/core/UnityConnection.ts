import * as net from 'net';
import * as dgram from 'dgram';
import { EventEmitter } from 'events';
import { JObject } from '../types/index.js';
import { McpErrorCode } from "../types/ErrorCodes.js";

/**
 * Handles TCP/IP communication between the TypeScript MCP server and Unity Editor.
 * Runs in server mode, accepting connections from multiple Unity clients.
 */
export class UnityConnection extends EventEmitter {
    private static instance: UnityConnection | null = null;
    private server: net.Server | null = null;
    private clients: Map<string, net.Socket> = new Map();
    private clientRefs: Map<net.Socket, { id: string }> = new Map();
    private activeClientId: string | null = null;
    private port: number = 27182; // Default port
    private host: string = '127.0.0.1';
    private pendingRequests: Map<string, { resolve: (value: JObject) => void, reject: (reason: Error) => void, timer: ReturnType<typeof setTimeout>, clientId: string }> = new Map();
    private requestId: number = 0;
    private clientDataBuffers: Map<string, string> = new Map();
    private clientInfoMap: Map<string, any> = new Map();

    // UDP broadcast related fields
    private broadcastSocket: dgram.Socket | null = null;
    private broadcastPort = 27183; // UDP broadcast port

    /**
     * Gets the singleton instance of the UnityConnection class.
     */
    public static getInstance(): UnityConnection {
        if (!UnityConnection.instance) {
            UnityConnection.instance = new UnityConnection();
        }
        return UnityConnection.instance;
    }

    private constructor() {
        super();

        // Error handler to prevent unhandled error events
        this.on('error', (err) => {
            // Just log in debug mode but don't crash
            console.error(`[DEBUG] Error event caught: ${err.message}`);
        });
    }

    /**
     * Configures the server settings.
     * @param host The host to bind to.
     * @param port The port to bind to.
     */
    public configure(host: string, port: number): void {
        this.host = host;
        this.port = port;
    }

    /**
     * Starts the server to accept Unity client connections.
     * @returns A promise that resolves when the server is started.
     */
    public start(): Promise<void> {
        return new Promise((resolve, reject) => {
            try {
                if (this.server) {
                    console.error('[INFO] Server is already running');
                    resolve();
                    return;
                }

                this.server = net.createServer((socket) => {
                    // New client connection
                    const clientId = `unity-${socket.remoteAddress}:${socket.remotePort}`;
                    console.error(`[INFO] New Unity client connected: ${clientId}`);

                    // Use a mutable ref so closures always see the current ID
                    // (handleRegistration updates ref.id when renaming)
                    const clientRef = { id: clientId };
                    this.clientRefs.set(socket, clientRef);

                    // Initialize buffer for this client
                    this.clientDataBuffers.set(clientId, '');

                    // Add client to the map
                    this.clients.set(clientId, socket);

                    // Set as active client if it's the first one
                    if (!this.activeClientId) {
                        this.activeClientId = clientId;
                        console.error(`[INFO] Set ${clientId} as active client`);
                    }

                    // Emit connection event
                    this.emit('clientConnected', {
                        clientId,
                        host: socket.remoteAddress,
                        port: socket.remotePort
                    });

                    // Set up data handling — closures read clientRef.id so they
                    // automatically pick up the renamed ID after registration
                    socket.on('data', (data) => this.handleClientData(clientRef.id, data));

                    // Handle disconnection
                    socket.on('close', () => {
                        const currentId = clientRef.id;
                        console.error(`[INFO] Unity client disconnected: ${currentId}`);

                        // Reject all pending requests for this client immediately
                        // instead of waiting for the 30-second timeout
                        this.rejectPendingRequestsForClient(currentId);

                        this.clients.delete(currentId);
                        this.clientDataBuffers.delete(currentId);
                        this.clientInfoMap.delete(currentId);
                        this.clientRefs.delete(socket);

                        // Update active client if this was the active one
                        if (this.activeClientId === currentId) {
                            this.activeClientId = this.clients.size > 0 ?
                                [...this.clients.keys()][0] : null;

                            if (this.activeClientId) {
                                console.error(`[INFO] New active client: ${this.activeClientId}`);
                            }
                        }

                        this.emit('clientDisconnected', { clientId: currentId });
                    });

                    // Handle errors
                    socket.on('error', (err) => {
                        console.error(`[ERROR] Socket error for client ${clientRef.id}: ${err.message}`);
                        this.emit('clientError', { clientId: clientRef.id, error: err });
                    });
                });

                // Handle server errors
                this.server.on('error', (err) => {
                    console.error(`[ERROR] Server error: ${err.message}`);
                    this.emit('error', err);
                    reject(err);
                });

                // Start listening
                this.server.listen(this.port, this.host, () => {
                    console.error(`[INFO] MCP server listening on ${this.host}:${this.port}`);
                    this.emit('serverStarted', { host: this.host, port: this.port });

                    // Send a single broadcast when server starts
                    this.sendInitialBroadcast("mcp_server_announce");

                    resolve();
                });
            } catch (err) {
                console.error(`[ERROR] Failed to start server: ${err instanceof Error ? err.message : String(err)}`);
                reject(err);
            }
        });
    }

    /**
     * Sends a single initial broadcast to announce the server
     */
    sendInitialBroadcast(type: string): void {
        try {
            // Create UDP socket for a single broadcast
            const socket = dgram.createSocket('udp4');

            socket.on('error', (err) => {
                console.error(`[ERROR] Broadcast socket error: ${err.message}`);
                try {
                    socket.close();
                } catch (e) {
                    // Ignore close errors
                }
            });

            socket.bind(0, () => {
                try {
                    // Enable broadcasting
                    socket.setBroadcast(true);

                    // Create server info message
                    const serverInfo = {
                        type: type,
                        host: this.host,
                        port: this.port,
                        version: "1.1.2",
                        protocol: "unity-mcp",
                        timestamp: Date.now()
                    };

                    const message = Buffer.from(JSON.stringify(serverInfo));

                    // Send the broadcast
                    socket.send(
                        message,
                        0,
                        message.length,
                        this.broadcastPort,
                        '255.255.255.255',
                        (err) => {
                            if (err) {
                                console.error(`[ERROR] Broadcast failed: ${err instanceof Error ? err.message : String(err)}`);
                            } else {
                                console.error('[INFO] Initial MCP server broadcast sent');
                            }

                            // Close the socket after sending regardless of success/failure
                            try {
                                socket.close();
                            } catch (e) {
                                // Ignore close errors
                            }
                        }
                    );
                } catch (err) {
                    console.error(`[ERROR] Failed to send broadcast: ${err instanceof Error ? err.message : String(err)}`);
                    try {
                        socket.close();
                    } catch (e) {
                        // Ignore close errors
                    }
                }
            });
        } catch (err) {
            console.error(`[ERROR] Failed to create broadcast socket: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /**
     * Handles data received from a Unity client.
     * @param clientId The client ID
     * @param data The received data
     */
    private handleClientData(clientId: string, data: Buffer): void {
        // Get the client's buffer
        let buffer = this.clientDataBuffers.get(clientId) || '';

        // Add received data to buffer
        buffer += data.toString('utf8');
        this.clientDataBuffers.set(clientId, buffer);

        // Process complete messages by newline delimiter
        let endIndex: number;
        while ((endIndex = buffer.indexOf('\n')) !== -1) {
            // Extract a complete message
            const message = buffer.substring(0, endIndex).trim();
            // Remove the processed message from the buffer
            buffer = buffer.substring(endIndex + 1);
            this.clientDataBuffers.set(clientId, buffer);

            // Process the message
            this.processClientMessage(clientId, message);
        }

        // Check if there's data in the buffer that might be a complete message without newline
        if (buffer.length > 0) {
            try {
                // Try to parse as JSON to see if it's complete
                JSON.parse(buffer);

                // If we reach here, it's valid JSON, so process it
                const message = buffer.trim();
                this.clientDataBuffers.set(clientId, '');
                this.processClientMessage(clientId, message);
            } catch (err) {
                // Not complete JSON, keep waiting for more data
            }
        }
    }

    /**
     * Processes a complete message from a Unity client.
     * @param clientId The client ID
     * @param message The message to process
     */
    private processClientMessage(clientId: string, message: string): void {
        if (!message) {
            return;
        }

        try {
            console.error(`[DEBUG] Processing message from ${clientId}: ${message}`);
            const response = JSON.parse(message) as JObject;

            // Handle registration message
            if (response.type === "registration") {
                this.handleRegistration(clientId, response);
                return;
            }

            // Handle success response with result and ID
            if (response.status === "success" && response.result && response.id) {
                const id = response.id as string;
                const result = response.result as JObject;

                // Resolve pending request and clear timeout
                const pending = this.pendingRequests.get(id);
                if (pending) {
                    clearTimeout(pending.timer);
                    this.pendingRequests.delete(id);
                    pending.resolve(result);
                }
            }
            // Handle error response from Unity
            else if (response.status === "error" && response.id) {
                const id = response.id as string;

                const pending = this.pendingRequests.get(id);
                if (pending) {
                    clearTimeout(pending.timer);
                    this.pendingRequests.delete(id);
                    const errorMsg = (response.error ?? response.message ?? "Unity returned an error") as string;
                    pending.reject(new Error(errorMsg));
                }
            }
            // Handle regular response with just an ID
            else if (response.id) {
                const id = response.id as string;

                // Resolve pending request and clear timeout
                const pending = this.pendingRequests.get(id);
                if (pending) {
                    clearTimeout(pending.timer);
                    this.pendingRequests.delete(id);
                    pending.resolve(response);
                }
            }
            // Handle push notification or event from Unity
            else {
                this.emit('message', { clientId, message: response });
            }
        } catch (err) {
            console.error(`[ERROR] Failed to parse message from ${clientId}: ${err instanceof Error ? err.message : String(err)}`);
        }
    }

    /**
     * Handles a client registration message
     * @param clientId The temporary client ID
     * @param message The registration message
     */
    private handleRegistration(clientId: string, message: JObject): void {
        // Get registration info
        const newClientId = message.clientId as string;
        const clientInfo = message.clientInfo;

        // Get existing socket
        const socket = this.clients.get(clientId);
        if (!socket) return;

        // If a different socket already owns this newClientId, evict it first
        if (newClientId !== clientId) {
            const existingSocket = this.clients.get(newClientId);
            if (existingSocket && existingSocket !== socket) {
                console.error(`[WARN] Client ID ${newClientId} already registered — disconnecting previous socket`);

                // Reject pending requests aimed at the old socket
                this.rejectPendingRequestsForClient(newClientId);

                // Clean up maps for the old socket
                this.clients.delete(newClientId);
                this.clientDataBuffers.delete(newClientId);
                this.clientInfoMap.delete(newClientId);
                this.clientRefs.delete(existingSocket);

                // Destroy the old socket (fires 'close' but maps are already cleaned)
                existingSocket.destroy();
            }
        }

        // Update from temporary ID to persistent ID
        this.clients.delete(clientId);
        this.clients.set(newClientId, socket);

        // Update buffer too
        const buffer = this.clientDataBuffers.get(clientId) || '';
        this.clientDataBuffers.delete(clientId);
        this.clientDataBuffers.set(newClientId, buffer);

        // Update the mutable ref so existing closures see the new ID
        const ref = this.clientRefs.get(socket);
        if (ref) {
            ref.id = newClientId;
        }

        // Store client info
        this.clientInfoMap.set(newClientId, clientInfo);

        // Log with minimal information (privacy-focused)
        console.error(`[INFO] Unity project registered: ${newClientId} (${(clientInfo as any)?.productName || 'Unknown'})`);

        // Update active client if needed
        if (this.activeClientId === clientId) {
            this.activeClientId = newClientId;
        }

        // Emit registration event
        this.emit('clientRegistered', { clientId: newClientId, info: clientInfo });
    }

    /**
     * Lists all connected Unity clients.
     * @returns An array of client information
     */
    public getConnectedClients(): Array<{ id: string, isActive: boolean, info: any }> {
        return Array.from(this.clients.keys()).map(id => ({
            id,
            isActive: id === this.activeClientId,
            info: this.clientInfoMap.get(id) || {}
        }));
    }

    /**
     * Clears all connected Unity clients.
     */
    public clearClients(): void {
        // Clear all client data
        this.clients.clear();
        this.clientRefs.clear();
        this.clientDataBuffers.clear();
        this.clientInfoMap.clear();
    }

    /**
     * Sets the active Unity client.
     * @param clientId The ID of the client to set as active
     * @returns True if successful, false if the client doesn't exist
     */
    public setActiveClient(clientId: string): boolean {
        if (!this.clients.has(clientId)) {
            return false;
        }

        this.activeClientId = clientId;
        console.error(`[INFO] Active client set to: ${clientId}`);
        this.emit('activeClientChanged', { clientId });
        return true;
    }

    /**
     * Gets the active client ID.
     * @returns The active client ID, or null if no connections
     */
    public getActiveClientId(): string | null {
        return this.activeClientId;
    }

    /**
     * Checks if there are any connected Unity clients.
     * @returns True if at least one client is connected
     */
    public hasConnectedClients(): boolean {
        return this.clients.size > 0;
    }

    /**
     * Sends a request to the active Unity client and waits for a response.
     * @param request The request object to send
     * @returns A Promise that resolves with the response
     */
    public async sendRequest(request: JObject): Promise<JObject> {
        if (!this.hasConnectedClients() || !this.activeClientId) {
            const error = new Error('No Unity clients connected');
            (error as any).code = McpErrorCode.ConnectionError;
            throw error;
        }

        return new Promise((resolve, reject) => {
            try {
                // Add request ID for tracking
                const id = (++this.requestId).toString();
                const activeClient = this.activeClientId as string;
                const requestWithId: JObject = {
                    command: request.command,
                    type: request.type || '',
                    params: request.params,
                    id
                };

                console.error(`[DEBUG] Sending request to ${activeClient}: ${JSON.stringify(requestWithId)}`);

                // Get the active client socket — re-check after initial guard
                // to handle race where client disconnects between check and write
                const socket = this.clients.get(activeClient);
                if (!socket) {
                    reject(new Error(`Client ${activeClient} disconnected before request could be sent`));
                    return;
                }

                // Set timeout to prevent hanging requests (store timer for cleanup)
                const timer = setTimeout(() => {
                    if (this.pendingRequests.has(id)) {
                        console.error(`[ERROR] Request with ID ${id} timed out`);
                        this.pendingRequests.delete(id);
                        reject(new Error('Request timed out'));
                    }
                }, 30000);

                // Store the promise callbacks with timer and client ID for cleanup
                this.pendingRequests.set(id, { resolve, reject, timer, clientId: activeClient });

                // Send the request
                const data = JSON.stringify(requestWithId) + '\n';
                socket.write(data, (err) => {
                    if (err) {
                        console.error(`[ERROR] Failed to send data to Unity: ${err.message}`);
                        const pending = this.pendingRequests.get(id);
                        if (pending) {
                            clearTimeout(pending.timer);
                            this.pendingRequests.delete(id);
                        }
                        reject(err);
                    }
                });
            } catch (err) {
                console.error(`[ERROR] Error sending request: ${err instanceof Error ? err.message : String(err)}`);
                reject(err);
            }
        });
    }

    /**
     * Checks if connected to any Unity client.
     * @returns True if connected, false otherwise
     */
    public isUnityConnected(): boolean {
        return this.hasConnectedClients();
    }

    /**
     * Ensures that there is an active connection to Unity, returning an error if not.
     * @returns A promise that resolves when connected or rejects if no connection
     */
    public async ensureConnected(): Promise<void> {
        if (!this.hasConnectedClients()) {
            const error = new Error('No Unity clients connected');
            (error as any).code = McpErrorCode.ConnectionError;
            throw error;
        }

        return Promise.resolve();
    }

    /**
     * Rejects all pending requests that were sent to a specific client.
     * Called when a client disconnects to give immediate error feedback
     * instead of waiting for the 30-second timeout.
     */
    private rejectPendingRequestsForClient(clientId: string): void {
        for (const [id, pending] of this.pendingRequests) {
            if (pending.clientId === clientId) {
                clearTimeout(pending.timer);
                this.pendingRequests.delete(id);
                pending.reject(new Error(`Unity client ${clientId} disconnected`));
            }
        }
    }

    /**
     * Stops the server and closes all connections.
     * Returns a promise that resolves when the server is fully closed.
     */
    public stop(): Promise<void> {
        // Reject all pending requests with timer cleanup
        for (const [id, pending] of this.pendingRequests) {
            clearTimeout(pending.timer);
            pending.reject(new Error('Connection closed'));
        }
        this.pendingRequests.clear();

        // Close all client connections
        for (const [clientId, socket] of this.clients.entries()) {
            console.error(`[INFO] Closing connection to client: ${clientId}`);
            socket.destroy();
        }

        this.clients.clear();
        this.clientRefs.clear();
        this.clientDataBuffers.clear();
        this.clientInfoMap.clear();
        this.activeClientId = null;

        // Close the server and wait for it to finish
        return new Promise((resolve) => {
            if (this.server) {
                this.server.close(() => {
                    console.error(`[INFO] Server stopped`);
                    this.emit('serverStopped');
                    resolve();
                });
                this.server = null;
            } else {
                resolve();
            }
        });
    }
}
