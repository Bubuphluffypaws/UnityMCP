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
            case "orbit":
            case "frameobject":
            case "setcamera":
            case "getcamera":
            case "inspectmaterial":
            case "previewtexture":
            case "setparameter":
            case "getparameters":
            case "listtoggles":
            case "setdefault":
            case "getanimparams":
            case "setanimparam":
            case "findobjects":
            case "inspectobject":
            case "getstate":
            case "searchlogs":
                return this.forwardToUnity(action, parameters);
            default:
                return {
                    success: false,
                    error: `Unknown action: ${action}`
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

        tools.set("scene_orbit", {
            description: "Orbits the Scene View camera around its current pivot. Positive yaw = rotate right, positive pitch = rotate up. Use with scene_screenshot for multi-angle visual iteration.",
            parameterSchema: {
                yaw: z.number().optional().describe("Horizontal rotation in degrees (positive = right)"),
                pitch: z.number().optional().describe("Vertical rotation in degrees (positive = up, clamped to ±89°)")
            },
            annotations: {
                title: "Orbit Camera",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_frameObject", {
            description: "Frames the Scene View on a specific GameObject. Centers the camera on the object's bounds. Optionally set camera angle and zoom.",
            parameterSchema: {
                object: z.string().describe("GameObject name or hierarchy path to frame on"),
                size: z.number().optional().describe("Camera zoom distance (smaller = closer)"),
                yaw: z.number().optional().describe("Set camera yaw angle in degrees (0 = front, 90 = right side, 180 = back)"),
                pitch: z.number().optional().describe("Set camera pitch angle in degrees (0 = level, negative = looking down)")
            },
            annotations: {
                title: "Frame Object",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_setCamera", {
            description: "Directly sets Scene View camera properties. All parameters optional — only specified values change.",
            parameterSchema: {
                pivotX: z.number().optional().describe("Camera orbit center X"),
                pivotY: z.number().optional().describe("Camera orbit center Y"),
                pivotZ: z.number().optional().describe("Camera orbit center Z"),
                yaw: z.number().optional().describe("Camera yaw angle in degrees"),
                pitch: z.number().optional().describe("Camera pitch angle in degrees"),
                size: z.number().optional().describe("Camera zoom distance"),
                orthographic: z.boolean().optional().describe("Enable orthographic projection")
            },
            annotations: {
                title: "Set Camera",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_getCamera", {
            description: "Returns the current Scene View camera state: pivot, rotation, size, and projection mode.",
            parameterSchema: {},
            annotations: {
                title: "Get Camera",
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

        tools.set("scene_listToggles", {
            description: "Lists all Modular Avatar MenuItems in the scene with their parameter name, isDefault state, isSynced, isSaved, and hierarchy path. Use isDefault to control which toggles are on by default — visible in edit mode Scene View.",
            parameterSchema: {},
            annotations: {
                title: "List MA Toggles",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_setDefault", {
            description: "Sets isDefault on a Modular Avatar MenuItem toggle. When isDefault=true, the toggle is ON by default — visible in edit mode Scene View without entering play mode. Find toggles by name or parameter.",
            parameterSchema: {
                name: z.string().optional().describe("GameObject name of the MenuItem (e.g. 'Eye Glow', 'Hair Circuit')"),
                parameter: z.string().optional().describe("VRC parameter name (e.g. 'AIEyeGlow') — alternative to name"),
                value: z.boolean().optional().describe("Set isDefault to true (on) or false (off). Default: true")
            },
            annotations: {
                title: "Set Toggle Default",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_getAnimParams", {
            description: "Gets all animator parameters and their current values. In play mode: reads live runtime values. In edit mode: reads controller defaults.",
            parameterSchema: {
                avatar: z.string().optional().describe("Avatar GameObject name (auto-detected if omitted)")
            },
            annotations: {
                title: "Get Animator Parameters",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_setAnimParam", {
            description: "Sets an animator parameter. In play mode: sets live value for immediate visual feedback (toggles, blends). In edit mode: sets controller default.",
            parameterSchema: {
                name: z.string().describe("Parameter name (e.g. 'AIEyeGlow', 'AIRim')"),
                value: z.union([z.boolean(), z.number()]).describe("Value to set (bool for toggles, float for blends, int for enums)"),
                avatar: z.string().optional().describe("Avatar GameObject name (auto-detected if omitted)")
            },
            annotations: {
                title: "Set Animator Parameter",
                readOnlyHint: false,
                openWorldHint: false
            }
        });

        tools.set("scene_findObjects", {
            description: "Finds GameObjects by name, component type, tag, or parent. Returns path, active state, child count, and component list for each match.",
            parameterSchema: {
                name: z.string().optional().describe("Filter by name (case-insensitive contains match)"),
                component: z.string().optional().describe("Filter by component type name (e.g. 'SkinnedMeshRenderer', 'ModularAvatarMenuItem')"),
                tag: z.string().optional().describe("Filter by Unity tag"),
                parent: z.string().optional().describe("Only return objects under this parent name"),
                maxResults: z.number().optional().describe("Max results to return (default: 50)"),
                includeInactive: z.boolean().optional().describe("Include inactive objects (default: true)")
            },
            annotations: {
                title: "Find GameObjects",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_inspectObject", {
            description: "Inspects a specific GameObject: transform, all components with key properties (renderer materials, mesh info, animator controller), and direct children list.",
            parameterSchema: {
                object: z.string().describe("GameObject name or hierarchy path")
            },
            annotations: {
                title: "Inspect GameObject",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_getState", {
            description: "Returns Unity editor state: play/pause/compiling mode, scene name, dirty flag, and list of VRC avatars in scene.",
            parameterSchema: {},
            annotations: {
                title: "Get Editor State",
                readOnlyHint: true,
                openWorldHint: false
            }
        });

        tools.set("scene_searchLogs", {
            description: "Searches console logs for a pattern without changing the persistent filter. Returns matching entries from newest to oldest.",
            parameterSchema: {
                pattern: z.string().describe("Text pattern to search for (e.g. '[AzukiAI]', 'error', 'NullReference')"),
                maxResults: z.number().optional().describe("Max matching entries to return (default: 50)"),
                caseSensitive: z.boolean().optional().describe("Case-sensitive search (default: false)")
            },
            annotations: {
                title: "Search Console Logs",
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
