import { z } from "zod";
import { IMcpToolDefinition } from "../core/interfaces/ICommandHandler.js";
import { JObject } from "../types/index.js";
import { BaseCommandHandler } from "../core/BaseCommandHandler.js";

/**
 * Command handler for executing C# code in the Unity editor.
 */
export class CodeExecutionCommandHandler extends BaseCommandHandler {
    public get commandPrefix(): string {
        return "code";
    }

    public get description(): string {
        return "Execute C# code in the Unity editor";
    }

    protected async executeCommand(action: string, parameters: JObject): Promise<JObject> {
        switch (action.toLowerCase()) {
            case "execute":
                return this.executeCode(parameters);
            default:
                return {
                    success: false,
                    error: `Unknown action: ${action}. Supported actions: execute`
                };
        }
    }

    public getToolDefinitions(): Map<string, IMcpToolDefinition> {
        const tools = new Map<string, IMcpToolDefinition>();

        tools.set("code_execute", {
            description: "Execute C# code in the Unity editor. Write direct executable code valid inside a method body — no using statements or class wrappers. Use return for value output. Full Unity and .NET API access.",
            parameterSchema: {
                code: z.string().describe("C# code to execute. Must be valid inside a method body — no using statements or class/method wrappers. Use return statements for value output. Has access to UnityEngine, UnityEditor, System, System.Linq, etc.")
            },
            annotations: {
                title: "Execute Code",
                readOnlyHint: false,
                destructiveHint: true,
                idempotentHint: false,
                openWorldHint: false
            }
        });

        return tools;
    }

    private async executeCode(parameters: JObject): Promise<JObject> {
        try {
            const code = parameters.code;
            if (!code) {
                return {
                    success: false,
                    error: "The 'code' parameter is required and cannot be empty."
                };
            }

            await this.ensureUnityConnection();
            return await this.sendUnityRequest(`${this.commandPrefix}.execute`, parameters);
        } catch (ex) {
            const errorMessage = ex instanceof Error ? ex.message : String(ex);
            console.error(`Error executing code: ${errorMessage}`);
            return {
                success: false,
                error: errorMessage
            };
        }
    }
}
