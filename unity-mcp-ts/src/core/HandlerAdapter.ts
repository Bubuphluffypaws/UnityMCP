import { ICommandHandler } from "./interfaces/ICommandHandler.js";
import { IResourceHandler } from "./interfaces/IResourceHandler.js";
import { IPromptHandler } from "./interfaces/IPromptHandler.js";
import { McpErrorCode } from "../types/ErrorCodes.js"
import {McpServer, ResourceTemplate} from "@modelcontextprotocol/sdk/server/mcp.js";

/**
 * Adapts various handler types to MCP SDK tools and resources.
 */
export class HandlerAdapter {
    private server: McpServer;

    /**
     * Initializes a new instance of the HandlerAdapter class.
     * @param server The MCP server to register tools with.
     */
    constructor(server: McpServer) {
        this.server = server;
    }

    /**
     * Registers a command handler with the MCP server.
     * @param handler The command handler to register.
     */
    public registerCommandHandler(handler: ICommandHandler): void {
        // Register tools if supported
        this.registerHandlerTools(handler);
    }

    /**
     * Registers a resource handler with the MCP server.
     * @param handler The resource handler to register.
     */
    public registerResourceHandler(handler: IResourceHandler): void {
        // Check if the URI template contains parameters - if so, use ResourceTemplate
        const hasParameters = handler.resourceUriTemplate.includes('{') && handler.resourceUriTemplate.includes('}');

        if (hasParameters) {

            // Create resource template
            // @ts-ignore
            const template = new ResourceTemplate(handler.resourceUriTemplate, {list:undefined});

            this.server.resource(
                handler.resourceName,
                template,
                async (uri, parameters) => {
                    try {
                        return await handler.fetchResource(uri, parameters);
                    } catch (error) {
                        const errorMessage = error instanceof Error ? error.message : String(error);
                        throw new Error(`Error fetching resource ${handler.resourceName}: ${errorMessage}`);
                    }
                }
            );
        } else {
            // Simple resource without parameters
            this.server.resource(
                handler.resourceName,
                handler.resourceUriTemplate,
                async (uri) => {
                    try {
                        return await handler.fetchResource(uri);
                    } catch (error) {
                        const errorMessage = error instanceof Error ? error.message : String(error);
                        throw new Error(`Error fetching resource ${handler.resourceName}: ${errorMessage}`);
                    }
                }
            );
        }

        console.error(`[INFO] Registered resource: ${handler.resourceName}`);
    }

    /**
     * Registers a prompt handler with the MCP server.
     * @param handler The prompt handler to register.
     */
    public registerPromptHandler(handler: IPromptHandler): void {
        // Skip if the handler doesn't support prompts
        const promptDefinitions = handler.getPromptDefinitions();
        if (!promptDefinitions) {
            return;
        }

        // Register each prompt definition
        for (const [promptName, definition] of promptDefinitions.entries()) {
            // Check if the prompt has additional parameters
            const hasParams = definition.additionalProperties && Object.keys(definition.additionalProperties).length > 0;

            if (hasParams) {
                this.server.prompt(
                    promptName,
                    definition.description,
                    definition.additionalProperties || {},
                    async (params) => {
                        return {
                            messages: [
                                {
                                    role: "user",
                                    content: {
                                        type: "text",
                                        text: this.applyTemplateParams(definition.template, params)
                                    }
                                }
                            ]
                        };
                    }
                );
            } else {
                this.server.prompt(
                    promptName,
                    definition.description,
                    async () => {
                        return {
                            messages: [
                                {
                                    role: "user",
                                    content: {
                                        type: "text",
                                        text: definition.template
                                    }
                                }
                            ]
                        };
                    }
                );
            }

            console.error(`[INFO] Registered prompt: ${promptName}`);
        }
    }

    /**
     * Apply parameters to a template string.
     * @param template The template string with {param} placeholders.
     * @param params The parameters to apply.
     * @returns The template with parameters applied.
     */
    private applyTemplateParams(template: string, params: Record<string, any>): string {
        let result = template;

        for (const [key, value] of Object.entries(params)) {
            result = result.replace(new RegExp(`{${key}}`, 'g'), String(value));
        }

        return result;
    }

    /**
     * Registers tools provided by the handler.
     * @param handler The command handler.
     */
    private registerHandlerTools(handler: ICommandHandler): void {
        // Skip if the handler doesn't support tools
        if (!handler.getToolDefinitions) {
            return;
        }

        const toolDefinitions = handler.getToolDefinitions();
        if (!toolDefinitions) {
            return;
        }

        // Register each tool definition
        for (const [toolName, definition] of toolDefinitions.entries()) {
            // Build args list carefully: the MCP SDK's tool() overload resolution
            // uses isZodRawShape to distinguish parameterSchema from annotations.
            // An empty {} has no ZodType values, so isZodRawShape returns false
            // and the SDK misinterprets it as annotations, shifting all arguments
            // and causing "cb is not a function". Only pass parameterSchema when
            // it has entries.
            const hasParams = definition.parameterSchema && Object.keys(definition.parameterSchema).length > 0;
            const args: any[] = [
                toolName,
                definition.description,
            ];
            if (hasParams) {
                args.push(definition.parameterSchema);
            }
            if (definition.annotations && Object.keys(definition.annotations).length > 0) {
                args.push(definition.annotations);
            }
            const toolCallback = async (params: any) => {
                try {
                    // Extract the action from the tool name (e.g., "menu_execute" -> "execute")
                    const action = toolName.split('_')[1] || 'execute';

                    // Execute the command and await the result
                    const result = await handler.execute(action, params);

                    if (result.success === false && result.error) {
                        return {
                            isError: true,
                            content: [{
                                type: "text",
                                text: `Error: ${result.error}`
                            }]
                        };
                    }

                    // Convert the result to a text response
                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify(result)
                        }]
                    };
                } catch (error) {
                    console.error(`[ERROR] Tool execution [${toolName}]: ${error instanceof Error ? error.message : String(error)}`);
                    return {
                        isError: true,
                        content: [{
                            type: "text",
                            text: `Error: ${error instanceof Error ? error.message : String(error)}`
                        }],
                        errorDetails: {
                            type: "execution_error",
                            timestamp: new Date().toISOString(),
                            command: `${toolName}`
                        }
                    };
                }
            };
            args.push(toolCallback);
            (this.server.tool as any)(...args);

            console.error(`[INFO] Registered tool: ${toolName}`);
        }
    }
}
