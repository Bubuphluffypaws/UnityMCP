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

        public string Description => "Scene view tools: screenshots, camera control, material inspection, texture preview, parameter control";

        public JObject Execute(string action, JObject parameters)
        {
            return action.ToLower() switch
            {
                "screenshot" => TakeScreenshot(parameters),
                "orbit" => OrbitCamera(parameters),
                "frameobject" => FrameObject(parameters),
                "setcamera" => SetCamera(parameters),
                "getcamera" => GetCamera(parameters),
                "inspectmaterial" => InspectMaterial(parameters),
                "previewtexture" => PreviewTexture(parameters),
                "setparameter" => SetParameter(parameters),
                "getparameters" => GetParameters(parameters),
                "listtoggles" => ListToggles(parameters),
                "setdefault" => SetDefault(parameters),
                "getanimparams" => GetAnimatorParams(parameters),
                "setanimparam" => SetAnimatorParam(parameters),
                "findobjects" => FindObjects(parameters),
                "inspectobject" => InspectObject(parameters),
                "getstate" => GetState(parameters),
                "searchlogs" => SearchLogs(parameters),
                _ => new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Unknown action: {action}. Supported: screenshot, orbit, frameObject, setCamera, getCamera, inspectMaterial, previewTexture, setParameter, getParameters, listToggles, setDefault, getAnimParams, setAnimParam, findObjects, inspectObject, getState, searchLogs"
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
        /// Orbits the Scene View camera around its current pivot point.
        /// Angles are in degrees. Positive yaw = rotate right, positive pitch = rotate up.
        /// </summary>
        private static JObject OrbitCamera(JObject parameters)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return new JObject { ["success"] = false, ["error"] = "No active Scene View." };

                var yaw = parameters["yaw"]?.Value<float>() ?? 0f;
                var pitch = parameters["pitch"]?.Value<float>() ?? 0f;

                var currentRot = sceneView.rotation;
                var euler = currentRot.eulerAngles;

                euler.y += yaw;
                euler.x += pitch;

                // Clamp pitch to avoid flipping
                if (euler.x > 180f) euler.x -= 360f;
                euler.x = Mathf.Clamp(euler.x, -89f, 89f);

                sceneView.rotation = Quaternion.Euler(euler);
                sceneView.Repaint();

                return new JObject
                {
                    ["success"] = true,
                    ["rotation"] = $"({euler.x:F1}, {euler.y:F1}, {euler.z:F1})",
                    ["pivot"] = FormatVector3(sceneView.pivot),
                    ["size"] = sceneView.size
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"Orbit failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Frames the Scene View on a specific GameObject by name or path.
        /// Optionally sets the camera distance (size).
        /// </summary>
        private static JObject FrameObject(JObject parameters)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return new JObject { ["success"] = false, ["error"] = "No active Scene View." };

                var objectName = parameters["object"]?.ToString();
                if (string.IsNullOrEmpty(objectName))
                    return new JObject { ["success"] = false, ["error"] = "Parameter 'object' (GameObject name or path) is required." };

                // Try full path first, then search by name
                var go = GameObject.Find(objectName);
                if (go == null)
                {
                    // Search all objects including inactive
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                    {
                        if (t.name == objectName)
                        {
                            go = t.gameObject;
                            break;
                        }
                    }
                }

                if (go == null)
                    return new JObject { ["success"] = false, ["error"] = $"GameObject '{objectName}' not found." };

                // Calculate bounds from all renderers on this object and children
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                bool hasBounds = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!hasBounds)
                    {
                        bounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                if (!hasBounds)
                {
                    // No renderers, just center on the transform
                    bounds = new Bounds(go.transform.position, Vector3.one * 0.5f);
                }

                // Frame on the bounds
                sceneView.Frame(bounds, false);

                // Override size if specified
                var size = parameters["size"]?.Value<float>();
                if (size.HasValue && size.Value > 0)
                {
                    sceneView.size = size.Value;
                }

                // Override rotation if specified
                var yaw = parameters["yaw"]?.Value<float>();
                var pitch = parameters["pitch"]?.Value<float>();
                if (yaw.HasValue || pitch.HasValue)
                {
                    var euler = sceneView.rotation.eulerAngles;
                    if (yaw.HasValue) euler.y = yaw.Value;
                    if (pitch.HasValue) euler.x = pitch.Value;
                    if (euler.x > 180f) euler.x -= 360f;
                    euler.x = Mathf.Clamp(euler.x, -89f, 89f);
                    sceneView.rotation = Quaternion.Euler(euler);
                }

                sceneView.Repaint();

                return new JObject
                {
                    ["success"] = true,
                    ["framed"] = objectName,
                    ["pivot"] = FormatVector3(sceneView.pivot),
                    ["size"] = sceneView.size,
                    ["rotation"] = FormatVector3(sceneView.rotation.eulerAngles)
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"FrameObject failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Directly sets the Scene View camera position, rotation, pivot, and zoom.
        /// All parameters are optional — only specified values are changed.
        /// </summary>
        private static JObject SetCamera(JObject parameters)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return new JObject { ["success"] = false, ["error"] = "No active Scene View." };

                // Pivot (orbit center point)
                if (parameters["pivotX"] != null || parameters["pivotY"] != null || parameters["pivotZ"] != null)
                {
                    var pivot = sceneView.pivot;
                    if (parameters["pivotX"] != null) pivot.x = parameters["pivotX"].Value<float>();
                    if (parameters["pivotY"] != null) pivot.y = parameters["pivotY"].Value<float>();
                    if (parameters["pivotZ"] != null) pivot.z = parameters["pivotZ"].Value<float>();
                    sceneView.pivot = pivot;
                }

                // Rotation (euler angles)
                if (parameters["yaw"] != null || parameters["pitch"] != null)
                {
                    var euler = sceneView.rotation.eulerAngles;
                    if (parameters["yaw"] != null) euler.y = parameters["yaw"].Value<float>();
                    if (parameters["pitch"] != null) euler.x = parameters["pitch"].Value<float>();
                    if (euler.x > 180f) euler.x -= 360f;
                    euler.x = Mathf.Clamp(euler.x, -89f, 89f);
                    sceneView.rotation = Quaternion.Euler(euler);
                }

                // Size (zoom distance)
                if (parameters["size"] != null)
                {
                    sceneView.size = parameters["size"].Value<float>();
                }

                // Orthographic mode
                if (parameters["orthographic"] != null)
                {
                    sceneView.orthographic = parameters["orthographic"].Value<bool>();
                }

                sceneView.Repaint();

                return new JObject
                {
                    ["success"] = true,
                    ["pivot"] = FormatVector3(sceneView.pivot),
                    ["rotation"] = FormatVector3(sceneView.rotation.eulerAngles),
                    ["size"] = sceneView.size,
                    ["orthographic"] = sceneView.orthographic
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"SetCamera failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Returns the current Scene View camera state.
        /// </summary>
        private static JObject GetCamera(JObject parameters)
        {
            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return new JObject { ["success"] = false, ["error"] = "No active Scene View." };

                var cam = sceneView.camera;
                return new JObject
                {
                    ["success"] = true,
                    ["pivot"] = FormatVector3(sceneView.pivot),
                    ["rotation"] = FormatVector3(sceneView.rotation.eulerAngles),
                    ["size"] = sceneView.size,
                    ["orthographic"] = sceneView.orthographic,
                    ["cameraPosition"] = cam != null ? FormatVector3(cam.transform.position) : "null",
                    ["nearClip"] = cam?.nearClipPlane,
                    ["farClip"] = cam?.farClipPlane
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"GetCamera failed: {ex.Message}" };
            }
        }

        private static string FormatVector3(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

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

        // ========== Hierarchy / State / Log Tools ==========

        /// <summary>
        /// Finds GameObjects by name, tag, component type, or path pattern.
        /// Returns name, path, active state, and component list for each match.
        /// </summary>
        private static JObject FindObjects(JObject parameters)
        {
            try
            {
                var nameFilter = parameters["name"]?.ToString();
                var tag = parameters["tag"]?.ToString();
                var component = parameters["component"]?.ToString();
                var parent = parameters["parent"]?.ToString();
                var maxResults = parameters["maxResults"]?.Value<int>() ?? 50;
                var includeInactive = parameters["includeInactive"]?.Value<bool>() ?? true;

                var results = new JArray();
                var allTransforms = UnityEngine.Object.FindObjectsOfType<Transform>(includeInactive);

                foreach (var t in allTransforms)
                {
                    if (results.Count >= maxResults) break;

                    // Filter by parent
                    if (!string.IsNullOrEmpty(parent))
                    {
                        bool underParent = false;
                        var cur = t.parent;
                        while (cur != null)
                        {
                            if (cur.name == parent || cur.gameObject.name == parent) { underParent = true; break; }
                            cur = cur.parent;
                        }
                        if (!underParent) continue;
                    }

                    // Filter by name (contains, case-insensitive)
                    if (!string.IsNullOrEmpty(nameFilter) &&
                        t.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    // Filter by tag
                    if (!string.IsNullOrEmpty(tag))
                    {
                        try { if (!t.CompareTag(tag)) continue; }
                        catch { continue; } // invalid tag
                    }

                    // Filter by component type
                    if (!string.IsNullOrEmpty(component))
                    {
                        bool hasComp = false;
                        foreach (var c in t.GetComponents<Component>())
                        {
                            if (c != null && c.GetType().Name.IndexOf(component, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hasComp = true;
                                break;
                            }
                        }
                        if (!hasComp) continue;
                    }

                    // Build path
                    var path = BuildPath(t);

                    // Component names
                    var comps = new JArray();
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c != null) comps.Add(c.GetType().Name);
                    }

                    results.Add(new JObject
                    {
                        ["name"] = t.name,
                        ["path"] = path,
                        ["active"] = t.gameObject.activeInHierarchy,
                        ["activeSelf"] = t.gameObject.activeSelf,
                        ["childCount"] = t.childCount,
                        ["components"] = comps
                    });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["count"] = results.Count,
                    ["results"] = results
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"FindObjects failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Inspects a specific GameObject: transform, all components with key properties,
        /// children list, and renderer/material info.
        /// </summary>
        private static JObject InspectObject(JObject parameters)
        {
            try
            {
                var objectPath = parameters["object"]?.ToString();
                if (string.IsNullOrEmpty(objectPath))
                    return new JObject { ["success"] = false, ["error"] = "Parameter 'object' (name or path) is required." };

                // Find by path first, then by name
                var go = GameObject.Find(objectPath);
                if (go == null)
                {
                    foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                    {
                        if (t.name == objectPath) { go = t.gameObject; break; }
                    }
                }
                if (go == null)
                    return new JObject { ["success"] = false, ["error"] = $"GameObject '{objectPath}' not found." };

                var result = new JObject
                {
                    ["success"] = true,
                    ["name"] = go.name,
                    ["path"] = BuildPath(go.transform),
                    ["active"] = go.activeInHierarchy,
                    ["activeSelf"] = go.activeSelf,
                    ["layer"] = LayerMask.LayerToName(go.layer),
                    ["tag"] = go.tag,
                    ["isStatic"] = go.isStatic
                };

                // Transform
                result["transform"] = new JObject
                {
                    ["localPosition"] = FormatVector3(go.transform.localPosition),
                    ["localRotation"] = FormatVector3(go.transform.localEulerAngles),
                    ["localScale"] = FormatVector3(go.transform.localScale),
                    ["worldPosition"] = FormatVector3(go.transform.position)
                };

                // Components with key properties
                var comps = new JArray();
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) { comps.Add(new JObject { ["type"] = "(missing script)" }); continue; }

                    var entry = new JObject { ["type"] = c.GetType().Name };

                    // Extract useful info from common component types
                    if (c is Renderer renderer)
                    {
                        entry["enabled"] = renderer.enabled;
                        var matNames = new JArray();
                        foreach (var m in renderer.sharedMaterials)
                            matNames.Add(m != null ? m.name : "null");
                        entry["materials"] = matNames;

                        if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        {
                            entry["mesh"] = smr.sharedMesh.name;
                            entry["vertexCount"] = smr.sharedMesh.vertexCount;
                            entry["blendShapeCount"] = smr.sharedMesh.blendShapeCount;
                        }
                    }
                    else if (c is Animator anim)
                    {
                        entry["controller"] = anim.runtimeAnimatorController != null
                            ? anim.runtimeAnimatorController.name : "null";
                        entry["enabled"] = anim.enabled;
                    }
                    else if (c is Behaviour behaviour)
                    {
                        entry["enabled"] = behaviour.enabled;
                    }

                    comps.Add(entry);
                }
                result["components"] = comps;

                // Children (direct)
                var children = new JArray();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i);
                    children.Add(new JObject
                    {
                        ["name"] = child.name,
                        ["active"] = child.gameObject.activeSelf,
                        ["childCount"] = child.childCount
                    });
                }
                result["children"] = children;
                result["childCount"] = go.transform.childCount;

                return result;
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"InspectObject failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Returns editor state: play mode, scene name, avatar info, connection status.
        /// </summary>
        private static JObject GetState(JObject parameters)
        {
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

                var result = new JObject
                {
                    ["success"] = true,
                    ["isPlaying"] = Application.isPlaying,
                    ["isPaused"] = EditorApplication.isPaused,
                    ["isCompiling"] = EditorApplication.isCompiling,
                    ["sceneName"] = scene.name,
                    ["scenePath"] = scene.path,
                    ["sceneDirty"] = scene.isDirty,
                    ["unityVersion"] = Application.unityVersion,
                    ["platform"] = Application.platform.ToString()
                };

                // Avatar info
                var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descType != null)
                {
                    var descriptors = UnityEngine.Object.FindObjectsOfType(descType, true);
                    var avatars = new JArray();
                    foreach (Component d in descriptors)
                    {
                        avatars.Add(new JObject
                        {
                            ["name"] = d.gameObject.name,
                            ["active"] = d.gameObject.activeInHierarchy
                        });
                    }
                    result["avatars"] = avatars;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"GetState failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Searches recent console logs for a pattern without changing the persistent filter.
        /// Returns matching log entries.
        /// </summary>
        private static JObject SearchLogs(JObject parameters)
        {
            try
            {
                var pattern = parameters["pattern"]?.ToString();
                if (string.IsNullOrEmpty(pattern))
                    return new JObject { ["success"] = false, ["error"] = "Parameter 'pattern' is required." };

                var maxResults = parameters["maxResults"]?.Value<int>() ?? 50;
                var caseSensitive = parameters["caseSensitive"]?.Value<bool>() ?? false;

                // Use LogEntries reflection (same as ConsoleCommandHandler)
                var logEntriesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
                var logEntryType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntriesType == null || logEntryType == null)
                    return new JObject { ["success"] = false, ["error"] = "LogEntries reflection not available." };

                var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Static);
                var startGetting = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static);
                var endGetting = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Public | BindingFlags.Static);
                var getEntry = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.Static);
                var messageField = logEntryType.GetField("message");
                var modeField = logEntryType.GetField("mode");

                if (getCount == null || startGetting == null || endGetting == null || getEntry == null || messageField == null)
                    return new JObject { ["success"] = false, ["error"] = "LogEntries methods not found." };

                var totalCount = (int)getCount.Invoke(null, null);
                startGetting.Invoke(null, null);

                try
                {
                    var matches = new JArray();
                    var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                    // Search from newest to oldest
                    for (int i = totalCount - 1; i >= 0 && matches.Count < maxResults; i--)
                    {
                        var logEntry = Activator.CreateInstance(logEntryType);
                        if (!(bool)getEntry.Invoke(null, new object[] { i, logEntry })) continue;

                        var message = (string)messageField.GetValue(logEntry);
                        if (message == null || message.IndexOf(pattern, comparison) < 0) continue;

                        // Truncate long messages
                        var displayMsg = message.Length > 500 ? message.Substring(0, 500) + "..." : message;

                        var entry = new JObject
                        {
                            ["row"] = i,
                            ["message"] = displayMsg
                        };
                        if (modeField != null) entry["mode"] = (int)modeField.GetValue(logEntry);

                        matches.Add(entry);
                    }

                    return new JObject
                    {
                        ["success"] = true,
                        ["pattern"] = pattern,
                        ["matchCount"] = matches.Count,
                        ["totalLogs"] = totalCount,
                        ["matches"] = matches
                    };
                }
                finally
                {
                    endGetting.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"SearchLogs failed: {ex.Message}" };
            }
        }

        private static string BuildPath(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts);
        }

        // ========== Toggle / Animator Tools ==========

        /// <summary>
        /// Lists all ModularAvatarMenuItem components in the scene with their
        /// parameter name, isDefault state, and parent hierarchy.
        /// </summary>
        private static JObject ListToggles(JObject parameters)
        {
            try
            {
                var maMenuItemType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuItem");
                if (maMenuItemType == null)
                    return new JObject { ["success"] = false, ["error"] = "ModularAvatarMenuItem type not found. Is Modular Avatar installed?" };

                var items = UnityEngine.Object.FindObjectsOfType(maMenuItemType, true);
                var result = new JArray();

                foreach (Component item in items)
                {
                    var go = item.gameObject;

                    // Read fields via reflection
                    var controlField = maMenuItemType.GetField("Control") ??
                                       maMenuItemType.GetProperty("Control")?.GetMethod != null
                                           ? null : null;
                    // Try property
                    object control = null;
                    var controlProp = maMenuItemType.GetProperty("Control");
                    if (controlProp != null)
                        control = controlProp.GetValue(item);
                    else
                    {
                        var cf = maMenuItemType.GetField("Control");
                        if (cf != null) control = cf.GetValue(item);
                    }

                    string paramName = "";
                    string controlType = "";
                    float controlValue = 0;

                    if (control != null)
                    {
                        var paramObj = control.GetType().GetField("parameter")?.GetValue(control);
                        if (paramObj != null)
                            paramName = paramObj.GetType().GetField("name")?.GetValue(paramObj)?.ToString() ?? "";

                        var typeVal = control.GetType().GetField("type")?.GetValue(control);
                        if (typeVal != null) controlType = typeVal.ToString();

                        var valueField = control.GetType().GetField("value");
                        if (valueField != null)
                            controlValue = Convert.ToSingle(valueField.GetValue(control));
                    }

                    // isDefault
                    var isDefaultField = maMenuItemType.GetField("isDefault");
                    bool isDefault = isDefaultField != null && (bool)isDefaultField.GetValue(item);

                    // isSynced, isSaved
                    var isSyncedField = maMenuItemType.GetField("isSynced");
                    bool isSynced = isSyncedField != null && (bool)isSyncedField.GetValue(item);

                    var isSavedField = maMenuItemType.GetField("isSaved");
                    bool isSaved = isSavedField != null && (bool)isSavedField.GetValue(item);

                    // Build path
                    var path = new System.Text.StringBuilder();
                    var t = go.transform;
                    while (t != null)
                    {
                        if (path.Length > 0) path.Insert(0, "/");
                        path.Insert(0, t.name);
                        t = t.parent;
                    }

                    result.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["path"] = path.ToString(),
                        ["parameter"] = paramName,
                        ["controlType"] = controlType,
                        ["value"] = controlValue,
                        ["isDefault"] = isDefault,
                        ["isSynced"] = isSynced,
                        ["isSaved"] = isSaved,
                        ["active"] = go.activeInHierarchy
                    });
                }

                return new JObject
                {
                    ["success"] = true,
                    ["count"] = result.Count,
                    ["toggles"] = result
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"ListToggles failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Sets isDefault on a ModularAvatarMenuItem by name or parameter name.
        /// Used for edit-mode toggle testing — set defaults, then rebuild/screenshot.
        /// </summary>
        private static JObject SetDefault(JObject parameters)
        {
            try
            {
                var targetName = parameters["name"]?.ToString();
                var targetParam = parameters["parameter"]?.ToString();
                var value = parameters["value"]?.Value<bool>() ?? true;

                if (string.IsNullOrEmpty(targetName) && string.IsNullOrEmpty(targetParam))
                    return new JObject { ["success"] = false, ["error"] = "Specify 'name' (GameObject name) or 'parameter' (VRC parameter name)." };

                var maMenuItemType = FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuItem");
                if (maMenuItemType == null)
                    return new JObject { ["success"] = false, ["error"] = "ModularAvatarMenuItem type not found." };

                var items = UnityEngine.Object.FindObjectsOfType(maMenuItemType, true);
                var isDefaultField = maMenuItemType.GetField("isDefault");
                if (isDefaultField == null)
                    return new JObject { ["success"] = false, ["error"] = "isDefault field not found on ModularAvatarMenuItem." };

                int matched = 0;
                var matchedNames = new List<string>();

                foreach (Component item in items)
                {
                    bool match = false;

                    // Match by GameObject name
                    if (!string.IsNullOrEmpty(targetName) &&
                        string.Equals(item.gameObject.name, targetName, StringComparison.OrdinalIgnoreCase))
                        match = true;

                    // Match by parameter name
                    if (!string.IsNullOrEmpty(targetParam))
                    {
                        var controlProp = maMenuItemType.GetProperty("Control");
                        object control = controlProp?.GetValue(item);
                        if (control == null)
                        {
                            var cf = maMenuItemType.GetField("Control");
                            control = cf?.GetValue(item);
                        }
                        if (control != null)
                        {
                            var paramObj = control.GetType().GetField("parameter")?.GetValue(control);
                            var pName = paramObj?.GetType().GetField("name")?.GetValue(paramObj)?.ToString() ?? "";
                            if (string.Equals(pName, targetParam, StringComparison.OrdinalIgnoreCase))
                                match = true;
                        }
                    }

                    if (match)
                    {
                        isDefaultField.SetValue(item, value);
                        EditorUtility.SetDirty(item);
                        matched++;
                        matchedNames.Add(item.gameObject.name);
                    }
                }

                if (matched == 0)
                    return new JObject { ["success"] = false, ["error"] = $"No matching MenuItem found for name='{targetName}' parameter='{targetParam}'." };

                return new JObject
                {
                    ["success"] = true,
                    ["matched"] = matched,
                    ["names"] = new JArray(matchedNames.ToArray()),
                    ["isDefault"] = value
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"SetDefault failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Gets all animator parameters and their current values from the avatar's Animator.
        /// Works in both edit mode (default values) and play mode (live values).
        /// </summary>
        private static JObject GetAnimatorParams(JObject parameters)
        {
            try
            {
                var avatarName = parameters["avatar"]?.ToString();
                var animator = FindAvatarAnimator(avatarName);
                if (animator == null)
                    return new JObject { ["success"] = false, ["error"] = "No avatar Animator found." };

                // In edit mode, read from the controller; in play mode, read live values
                bool isPlaying = Application.isPlaying;
                var result = new JArray();

                if (isPlaying && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                {
                    // Play mode — read live parameter values
                    foreach (var param in animator.parameters)
                    {
                        var entry = new JObject
                        {
                            ["name"] = param.name,
                            ["type"] = param.type.ToString()
                        };

                        switch (param.type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                entry["value"] = animator.GetBool(param.name);
                                break;
                            case AnimatorControllerParameterType.Int:
                                entry["value"] = animator.GetInteger(param.name);
                                break;
                            case AnimatorControllerParameterType.Float:
                                entry["value"] = animator.GetFloat(param.name);
                                break;
                            case AnimatorControllerParameterType.Trigger:
                                entry["value"] = false; // triggers are transient
                                break;
                        }

                        result.Add(entry);
                    }
                }
                else
                {
                    // Edit mode — read from controller definition
                    var ctrl = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                    if (ctrl != null)
                    {
                        foreach (var param in ctrl.parameters)
                        {
                            var entry = new JObject
                            {
                                ["name"] = param.name,
                                ["type"] = param.type.ToString()
                            };

                            switch (param.type)
                            {
                                case AnimatorControllerParameterType.Bool:
                                    entry["value"] = param.defaultBool;
                                    break;
                                case AnimatorControllerParameterType.Int:
                                    entry["value"] = param.defaultInt;
                                    break;
                                case AnimatorControllerParameterType.Float:
                                    entry["value"] = param.defaultFloat;
                                    break;
                            }

                            result.Add(entry);
                        }
                    }
                }

                return new JObject
                {
                    ["success"] = true,
                    ["avatar"] = animator.gameObject.name,
                    ["isPlaying"] = isPlaying,
                    ["parameterCount"] = result.Count,
                    ["parameters"] = result
                };
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"GetAnimParams failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Sets an animator parameter on the avatar. In play mode, sets the live
        /// value on the Animator. In edit mode, sets the default on the controller.
        /// </summary>
        private static JObject SetAnimatorParam(JObject parameters)
        {
            try
            {
                var paramName = parameters["name"]?.ToString();
                if (string.IsNullOrEmpty(paramName))
                    return new JObject { ["success"] = false, ["error"] = "Parameter 'name' is required." };

                var value = parameters["value"];
                if (value == null)
                    return new JObject { ["success"] = false, ["error"] = "Parameter 'value' is required." };

                var avatarName = parameters["avatar"]?.ToString();
                var animator = FindAvatarAnimator(avatarName);
                if (animator == null)
                    return new JObject { ["success"] = false, ["error"] = "No avatar Animator found." };

                bool isPlaying = Application.isPlaying;

                if (isPlaying && animator.isActiveAndEnabled)
                {
                    // Play mode — set live value
                    // Determine type from the parameter
                    foreach (var param in animator.parameters)
                    {
                        if (param.name != paramName) continue;

                        switch (param.type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                var boolVal = value.Type == JTokenType.Boolean
                                    ? value.Value<bool>()
                                    : value.Value<float>() > 0.5f;
                                animator.SetBool(paramName, boolVal);
                                return new JObject
                                {
                                    ["success"] = true,
                                    ["mode"] = "playMode",
                                    ["parameter"] = paramName,
                                    ["value"] = boolVal
                                };
                            case AnimatorControllerParameterType.Int:
                                var intVal = value.Value<int>();
                                animator.SetInteger(paramName, intVal);
                                return new JObject
                                {
                                    ["success"] = true,
                                    ["mode"] = "playMode",
                                    ["parameter"] = paramName,
                                    ["value"] = intVal
                                };
                            case AnimatorControllerParameterType.Float:
                                var floatVal = value.Value<float>();
                                animator.SetFloat(paramName, floatVal);
                                return new JObject
                                {
                                    ["success"] = true,
                                    ["mode"] = "playMode",
                                    ["parameter"] = paramName,
                                    ["value"] = floatVal
                                };
                            case AnimatorControllerParameterType.Trigger:
                                animator.SetTrigger(paramName);
                                return new JObject
                                {
                                    ["success"] = true,
                                    ["mode"] = "playMode",
                                    ["parameter"] = paramName,
                                    ["triggered"] = true
                                };
                        }
                    }

                    return new JObject { ["success"] = false, ["error"] = $"Parameter '{paramName}' not found on Animator." };
                }
                else
                {
                    // Edit mode — set default on controller
                    var ctrl = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                    if (ctrl == null)
                        return new JObject { ["success"] = false, ["error"] = "No AnimatorController found in edit mode." };

                    var ctrlParams = ctrl.parameters;
                    for (int i = 0; i < ctrlParams.Length; i++)
                    {
                        if (ctrlParams[i].name != paramName) continue;

                        switch (ctrlParams[i].type)
                        {
                            case AnimatorControllerParameterType.Bool:
                                ctrlParams[i].defaultBool = value.Type == JTokenType.Boolean
                                    ? value.Value<bool>()
                                    : value.Value<float>() > 0.5f;
                                break;
                            case AnimatorControllerParameterType.Int:
                                ctrlParams[i].defaultInt = value.Value<int>();
                                break;
                            case AnimatorControllerParameterType.Float:
                                ctrlParams[i].defaultFloat = value.Value<float>();
                                break;
                        }

                        ctrl.parameters = ctrlParams;
                        EditorUtility.SetDirty(ctrl);

                        return new JObject
                        {
                            ["success"] = true,
                            ["mode"] = "editMode",
                            ["parameter"] = paramName,
                            ["value"] = value
                        };
                    }

                    return new JObject { ["success"] = false, ["error"] = $"Parameter '{paramName}' not found on controller." };
                }
            }
            catch (Exception ex)
            {
                return new JObject { ["success"] = false, ["error"] = $"SetAnimParam failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Finds the Animator on the VRC avatar descriptor, or by name.
        /// </summary>
        private static Animator FindAvatarAnimator(string avatarName = null)
        {
            // If name specified, find that specific object
            if (!string.IsNullOrEmpty(avatarName))
            {
                var go = GameObject.Find(avatarName);
                if (go != null)
                {
                    var anim = go.GetComponent<Animator>();
                    if (anim != null) return anim;
                }
            }

            // Find VRCAvatarDescriptor
            var descType = FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descType != null)
            {
                var descriptor = UnityEngine.Object.FindObjectOfType(descType) as Component;
                if (descriptor != null)
                {
                    return descriptor.GetComponent<Animator>();
                }
            }

            return null;
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
