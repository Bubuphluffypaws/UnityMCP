import { z } from "zod";
import { IMcpToolDefinition } from "../core/interfaces/ICommandHandler.js";
import { JObject } from "../types/index.js";
import { BaseCommandHandler } from "../core/BaseCommandHandler.js";

/**
 * Command handler for scene inspection and visual feedback tools.
 * Provides screenshots, material inspection, texture preview, and
 * VRC parameter control for iterative avatar development.
 */
export class SceneCommandHandler extends BaseCommandHandler {
    public get commandPrefix(): string {
        return "scene";
    }

    public get description(): string {
        return "Scene view tools: screenshots, material inspection, texture preview, parameter control";
    }

    protected async executeCommand(action: string, parameters: JObject): Promise<JObject> {
        switch (action.toLowerCase()) {
            case "screenshot":
            case "inspectmaterial":
            case "previewtexture":
            case "setparameter":
            case "getparameters":
                return this.forwardToUnity(action, parameters);
            default:
                return {
                    success: false,
                    error: `Unknown action: ${action}. Supported: screenshot, inspectMaterial, previewTexture, setParameter, getParameters`
                };
        }
    }

    public getToolDefinitions(): Map<string, IMcpToolDefinition> {
        const tools = new Map<string, IMcpToolDefinition>();

        tools.set("scene_screenshot", {
            description: "Captures the Unity Scene View as a PNG screenshot. Returns the file path to the saved image.",
            parameterSchema: {
                width: z.number().optional().describe("Screenshot width in pixels (default: 1024)"),
                height: z.number().optional().describe("Screenshot height in pixels (default: 768)"),
                outputPath: z.string().optional().describe("Output file path (default: temp directory)")
            },
            annotations: {
                title: "Take Scene Screenshot",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_inspectMaterial", {
            description: "Inspects a material's shader and all properties (floats, colors, textures, vectors). Find by material name or renderer GameObject path.",
            parameterSchema: {
                material: z.string().optional().describe("Material name to inspect (e.g. 'Azuki_Body')"),
                renderer: z.string().optional().describe("GameObject path with a Renderer component (alternative to material name)"),
                materialIndex: z.number().optional().describe("Material slot index on the renderer (default: 0)")
            },
            annotations: {
                title: "Inspect Material",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_previewTexture", {
            description: "Exports a texture asset to a temp PNG file for viewing. Works with non-readable textures.",
            parameterSchema: {
                path: z.string().optional().describe("Asset path to the texture (e.g. 'Assets/Textures/MyTex.png')"),
                name: z.string().optional().describe("Texture name to search for (alternative to path)")
            },
            annotations: {
                title: "Preview Texture",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_setParameter", {
            description: "Sets a VRC expression parameter on the avatar. Uses Gesture Manager if available (works in edit mode), otherwise falls back to Animator.",
            parameterSchema: {
                name: z.string().describe("The parameter name (e.g. 'AIEyeGlow', 'AIRim')"),
                value: z.union([z.boolean(), z.number()]).describe("The value to set (bool for toggles, float for radials)")
            },
            annotations: {
                title: "Set VRC Parameter",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_getParameters", {
            description: "Lists all synced VRC expression parameters on the avatar with their types, defaults, and sync settings.",
            parameterSchema: {},
            annotations: {
                title: "Get VRC Parameters",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        return tools;
    }

    private async forwardToUnity(action: string, parameters: JObject): Promise<JObject> {
        try {
            await this.ensureUnityConnection();
            return await this.sendUnityRequest(`${this.commandPrefix}.${action}`, parameters);
        } catch (ex) {
            const errorMessage = ex instanceof Error ? ex.message : String(ex);
            console.error(`Error in scene.${action}: ${errorMessage}`);
            return {
                success: false,
                error: errorMessage
            };
        }
    }
}
