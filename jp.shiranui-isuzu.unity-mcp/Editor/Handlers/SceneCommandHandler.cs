using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Handlers
{
    /// <summary>
    /// Command handler for scene inspection, screenshots, material inspection,
    /// texture preview, and Gesture Manager parameter control.
    /// </summary>
    internal sealed class SceneCommandHandler : IMcpCommandHandler
    {
        public string CommandPrefix => "scene";

        public string Description => "Scene view tools: screenshots, material inspection, texture preview, parameter control";

        public JObject Execute(string action, JObject parameters)
        {
            return action.ToLower() switch
            {
                "screenshot" => TakeScreenshot(parameters),
                "inspectmaterial" => InspectMaterial(parameters),
                "previewtexture" => PreviewTexture(parameters),
                "setparameter" => SetParameter(parameters),
                "getparameters" => GetParameters(parameters),
                _ => new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Unknown action: {action}. Supported: screenshot, inspectMaterial, previewTexture, setParameter, getParameters"
                }
            };
        }

        /// <summary>
        /// Captures the Scene View as a PNG screenshot.
        /// Returns the file path so the caller can read the image.
        /// </summary>
        private static JObject TakeScreenshot(JObject parameters)
        {
            try
            {
                var width = parameters["width"]?.Value<int>() ?? 1024;
                var height = parameters["height"]?.Value<int>() ?? 768;
                var outputPath = parameters["outputPath"]?.ToString();

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.Combine(Path.GetTempPath(), $"unity_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                }

                // Try Scene View first, fall back to Game View
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "No active Scene View found. Open a Scene View window in Unity."
                    };
                }

                // Force repaint to ensure current state is rendered
                sceneView.Repaint();

                var cam = sceneView.camera;
                if (cam == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Scene View camera not available."
                    };
                }

                // Render to texture
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                var prevRT = cam.targetTexture;
                var prevActive = RenderTexture.active;

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                // Restore
                cam.targetTexture = prevRT;
                RenderTexture.active = prevActive;

                // Save
                var pngBytes = tex.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngBytes);

                // Cleanup
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);

                return new JObject
                {
                    ["success"] = true,
                    ["path"] = outputPath,
                    ["width"] = width,
                    ["height"] = height,
                    ["sizeBytes"] = pngBytes.Length
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Screenshot failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Inspects a material by name or renderer path, returning all properties.
        /// </summary>
        private static JObject InspectMaterial(JObject parameters)
        {
            try
            {
                var materialName = parameters["material"]?.ToString();
                var rendererPath = parameters["renderer"]?.ToString();
                var materialIndex = parameters["materialIndex"]?.Value<int>() ?? 0;

                Material mat = null;

                if (!string.IsNullOrEmpty(materialName))
                {
                    // Find by material name
                    var guids = AssetDatabase.FindAssets($"t:Material {materialName}");
                    foreach (var guid in guids)
                    {
                        var candidate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                        if (candidate != null && candidate.name == materialName)
                        {
                            mat = candidate;
                            break;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(rendererPath))
                {
                    // Find by renderer in scene
                    var go = GameObject.Find(rendererPath);
                    if (go != null)
                    {
                        var renderer = go.GetComponent<Renderer>();
                        if (renderer != null && materialIndex < renderer.sharedMaterials.Length)
                        {
                            mat = renderer.sharedMaterials[materialIndex];
                        }
                    }
                }

                if (mat == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = $"Material not found. Specify 'material' (name) or 'renderer' (GameObject path) + 'materialIndex'."
                    };
                }

                // Build property list from shader
                var result = new JObject
                {
                    ["success"] = true,
                    ["name"] = mat.name,
                    ["shader"] = mat.shader.name,
                    ["renderQueue"] = mat.renderQueue
                };

                var props = new JObject();
                var shader = mat.shader;
                var propCount = shader.GetPropertyCount();

                for (int i = 0; i < propCount; i++)
                {
                    var propName = shader.GetPropertyName(i);
                    var propType = shader.GetPropertyType(i);
                    var propDesc = shader.GetPropertyDescription(i);

                    var prop = new JObject { ["description"] = propDesc };

                    switch (propType)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            prop["type"] = propType == UnityEngine.Rendering.ShaderPropertyType.Range ? "range" : "float";
                            prop["value"] = mat.GetFloat(propName);
                            if (propType == UnityEngine.Rendering.ShaderPropertyType.Range)
                            {
                                var range = shader.GetPropertyRangeLimits(i);
                                prop["min"] = range.x;
                                prop["max"] = range.y;
                            }
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            prop["type"] = "color";
                            var col = mat.GetColor(propName);
                            prop["value"] = $"RGBA({col.r:F3}, {col.g:F3}, {col.b:F3}, {col.a:F3})";
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                            prop["type"] = "vector";
                            var vec = mat.GetVector(propName);
                            prop["value"] = $"({vec.x:F3}, {vec.y:F3}, {vec.z:F3}, {vec.w:F3})";
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            prop["type"] = "texture";
                            var tex = mat.GetTexture(propName);
                            prop["value"] = tex != null ? tex.name : "null";
                            if (tex != null)
                            {
                                prop["texWidth"] = tex.width;
                                prop["texHeight"] = tex.height;
                            }
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Int:
                            prop["type"] = "int";
                            prop["value"] = mat.GetInt(propName);
                            break;
                    }

                    props[propName] = prop;
                }

                result["properties"] = props;
                result["propertyCount"] = propCount;

                return result;
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Material inspection failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Saves a texture asset to a temp PNG for preview.
        /// </summary>
        private static JObject PreviewTexture(JObject parameters)
        {
            try
            {
                var texturePath = parameters["path"]?.ToString();
                var textureName = parameters["name"]?.ToString();

                Texture2D tex = null;

                if (!string.IsNullOrEmpty(texturePath))
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                }
                else if (!string.IsNullOrEmpty(textureName))
                {
                    var guids = AssetDatabase.FindAssets($"t:Texture2D {textureName}");
                    foreach (var guid in guids)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var candidate = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (candidate != null && candidate.name == textureName)
                        {
                            tex = candidate;
                            texturePath = path;
                            break;
                        }
                    }
                }

                if (tex == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Texture not found. Specify 'path' (asset path) or 'name'."
                    };
                }

                // For non-readable textures, use a RenderTexture copy
                var outputPath = Path.Combine(Path.GetTempPath(), $"unity_tex_preview_{tex.name}.png");

                if (tex.isReadable)
                {
                    File.WriteAllBytes(outputPath, tex.EncodeToPNG());
                }
                else
                {
                    // Copy via RenderTexture for non-readable textures
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(tex, rt);
                    var prevActive = RenderTexture.active;
                    RenderTexture.active = rt;
                    var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    readable.Apply();
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(rt);

                    File.WriteAllBytes(outputPath, readable.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(readable);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["path"] = outputPath,
                    ["assetPath"] = texturePath,
                    ["name"] = tex.name,
                    ["width"] = tex.width,
                    ["height"] = tex.height
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Texture preview failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Sets a parameter on the avatar via Gesture Manager or direct Animator.
        /// Works in edit mode if Gesture Manager is present.
        /// </summary>
        private static JObject SetParameter(JObject parameters)
        {
            try
            {
                var paramName = parameters["name"]?.ToString();
                if (string.IsNullOrEmpty(paramName))
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Parameter 'name' is required."
                    };
                }

                var value = parameters["value"];
                if (value == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Parameter 'value' is required."
                    };
                }

                // Try Gesture Manager first (edit mode parameter control)
                var gmResult = TryGestureManager(paramName, value);
                if (gmResult != null) return gmResult;

                // Fallback: find VRCAvatarDescriptor and set on its Animator
                var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descType != null)
                {
                    var descriptor = UnityEngine.Object.FindObjectOfType(descType) as Component;
                    if (descriptor != null)
                    {
                        var animator = descriptor.GetComponent<Animator>();
                        if (animator != null && animator.runtimeAnimatorController != null)
                        {
                            if (value.Type == JTokenType.Boolean || value.Type == JTokenType.Integer)
                            {
                                animator.SetBool(paramName, value.Value<bool>());
                            }
                            else if (value.Type == JTokenType.Float)
                            {
                                animator.SetFloat(paramName, value.Value<float>());
                            }

                            return new JObject
                            {
                                ["success"] = true,
                                ["method"] = "animator",
                                ["parameter"] = paramName,
                                ["value"] = value
                            };
                        }
                    }
                }

                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "No Gesture Manager or VRC avatar Animator found."
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"SetParameter failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Gets all synced expression parameters from the avatar descriptor.
        /// </summary>
        private static JObject GetParameters(JObject parameters)
        {
            try
            {
                var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descType == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "VRCAvatarDescriptor type not found."
                    };
                }

                var descriptor = UnityEngine.Object.FindObjectOfType(descType) as Component;
                if (descriptor == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "No VRCAvatarDescriptor found in scene."
                    };
                }

                // Get expressionParameters via reflection
                var exprParamsProp = descType.GetProperty("expressionParameters") ??
                                     descType.GetField("expressionParameters")?.FieldType != null
                                         ? null : null;

                // Try field access
                var exprParamsField = descType.GetField("expressionParameters");
                if (exprParamsField == null)
                {
                    // Try property
                    var pi = descType.GetProperty("expressionParameters");
                    if (pi == null)
                    {
                        return new JObject
                        {
                            ["success"] = false,
                            ["error"] = "Cannot access expressionParameters on avatar descriptor."
                        };
                    }
                }

                var exprParams = exprParamsField?.GetValue(descriptor);
                if (exprParams == null)
                {
                    return new JObject
                    {
                        ["success"] = true,
                        ["parameters"] = new JArray(),
                        ["note"] = "No expression parameters asset assigned."
                    };
                }

                // Get parameters array
                var paramsField = exprParams.GetType().GetField("parameters");
                if (paramsField == null)
                {
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = "Cannot access parameters array."
                    };
                }

                var paramsArray = paramsField.GetValue(exprParams) as Array;
                var result = new JArray();

                if (paramsArray != null)
                {
                    foreach (var p in paramsArray)
                    {
                        var nameField = p.GetType().GetField("name");
                        var typeField = p.GetType().GetField("valueType");
                        var defaultField = p.GetType().GetField("defaultValue");
                        var savedField = p.GetType().GetField("saved");
                        var syncedField = p.GetType().GetField("networkSynced");

                        var entry = new JObject
                        {
                            ["name"] = nameField?.GetValue(p)?.ToString() ?? "",
                            ["type"] = typeField?.GetValue(p)?.ToString() ?? "",
                            ["default"] = defaultField?.GetValue(p) != null ? JToken.FromObject(defaultField.GetValue(p)) : 0,
                            ["saved"] = savedField?.GetValue(p) is bool b && b,
                            ["synced"] = syncedField?.GetValue(p) is bool s && s
                        };

                        // Skip empty parameter slots
                        if (!string.IsNullOrEmpty(entry["name"]?.ToString()))
                        {
                            result.Add(entry);
                        }
                    }
                }

                return new JObject
                {
                    ["success"] = true,
                    ["avatar"] = descriptor.gameObject.name,
                    ["parameterCount"] = result.Count,
                    ["parameters"] = result
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"GetParameters failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Attempts to set a parameter via Gesture Manager if available.
        /// </summary>
        private static JObject TryGestureManager(string paramName, JToken value)
        {
            // Look for GestureManager in the scene
            var gmType = FindType("BlackStartX.GestureManager.GestureManager");
            if (gmType == null) return null;

            var gm = UnityEngine.Object.FindObjectOfType(gmType);
            if (gm == null) return null;

            // Try to find the SetValue method or similar API
            var setMethod = gmType.GetMethod("SetValue",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(float) }, null);

            if (setMethod != null)
            {
                var floatVal = value.Type == JTokenType.Boolean
                    ? (value.Value<bool>() ? 1f : 0f)
                    : value.Value<float>();

                setMethod.Invoke(gm, new object[] { paramName, floatVal });
                return new JObject
                {
                    ["success"] = true,
                    ["method"] = "gestureManager",
                    ["parameter"] = paramName,
                    ["value"] = floatVal
                };
            }

            return null; // GestureManager found but API not compatible
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }
    }
}
