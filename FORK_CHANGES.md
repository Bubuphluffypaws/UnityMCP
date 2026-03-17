# Bubuphluffypaws Fork — Changes & Features

Fork of [isuzu-shiranui/UnityMCP](https://github.com/isuzu-shiranui/UnityMCP) with 24 bug fixes, 18 new scene tools, and VRChat workflow integration.

## Bug Fixes (24 issues, all fixed)

### Connection & Reliability
| Issue | Description | Branch |
|-------|-------------|--------|
| #1 | Commands hang 30s when Unity disconnects mid-request | fix/server-stability |
| #2 | Race condition between connection check and socket write | fix/server-stability |
| #3 | Domain reload destroys MCP connection with no recovery | fix/client-reliability |
| #4 | UDP discovery overwrites host with non-routable 0.0.0.0 | fix/client-reliability |
| #5 | First command after reconnect fails due to stale data buffer | fix/client-reliability |
| #8 | Checking connected clients causes disconnect | fix/client-reliability |
| #10 | Port not released after crash or shutdown | fix/server-stability |

### Thread Safety & Memory
| Issue | Description | Branch |
|-------|-------------|--------|
| #6 | Missing volatile, unsynchronized shared fields | fix/thread-safety |
| #7 | ManualResetEvent handles leaked on every command | fix/thread-safety |
| #13 | Unsynchronized TcpClient, ManualResetEvent use-after-dispose, Stop/Start races | fix/thread-safety |
| #23 | ProcessIncomingData race with Stop(), unbounded buffer growth, handle leak on timeout | fix/server-robustness |

### Server & Protocol
| Issue | Description | Branch |
|-------|-------------|--------|
| #9 | Server stays alive in broken state after uncaught exceptions | fix/server-stability |
| #11 | Crash recovery causes handler-less McpServer (Unknown command prefix) | fix/crash-recovery-and-empty-schema |
| #15 | TS: Speculative JSON parse, stop/start server races | fix/tcp-framing |
| #23 | Error response missing request ID — TS side hangs 30s | fix/server-robustness |
| #23 | code_execute timeout bumped to 30s (was 5s) | fix/server-robustness |

### Handler & Tool Registration
| Issue | Description | Branch |
|-------|-------------|--------|
| #12 | console_getCount and console_clear return "cb is not a function" | fix/tool-registration |
| #14 | TS: Client registration renames ID but socket closures keep old ID | fix/client-registration |
| #16 | Reflection null safety in ConsoleCommandHandler | fix/reflection-safety |
| #17 | Handler execution safety: modal dialogs, spin-wait freeze | fix/handler-execution |
| #19 | TS: Empty parameterSchema bypasses HandlerAdapter fix | fix/tool-registration |
| #24 | menu_execute fails for built-in Unity menu items (Assets/Refresh, etc.) | fix/menu-item-detection |

### Settings & UI
| Issue | Description | Branch |
|-------|-------------|--------|
| #18 | Settings serialization: Dictionary not serializable, stale refs | fix/settings-serialization |
| #20 | Minor: buffer size, Path.Combine, dead code cleanup | (open) |
| #21 | Start() doesn't re-register EditorApplication.update after Stop() | fix/start-callback |
| #22 | Settings UI crashes after domain reload (null styles) | fix/settings-null-safety |

## New Features: Scene Tools (feature/scene-tools)

18 new MCP tools for iterative Unity/VRChat development. Both C# handler (`SceneCommandHandler.cs`) and TypeScript bridge (`SceneCommandHandler.ts`).

### Visual Feedback
| Tool | Description |
|------|-------------|
| `scene_screenshot` | Capture Scene View as PNG |
| `scene_orbit` | Rotate camera by yaw/pitch |
| `scene_frameObject` | Frame on a named GameObject with angle/zoom |
| `scene_setCamera` | Set pivot, rotation, size, orthographic mode |
| `scene_getCamera` | Read current camera state |

### Material & Texture Inspection
| Tool | Description |
|------|-------------|
| `scene_inspectMaterial` | All shader properties as structured JSON (floats, colors, textures, vectors) |
| `scene_previewTexture` | Export any texture to viewable PNG (handles non-readable textures) |

### VRChat Toggle Testing
| Tool | Description |
|------|-------------|
| `scene_listToggles` | List all Modular Avatar MenuItems with isDefault/param/sync state |
| `scene_setDefault` | Set isDefault on MA toggles (edit mode preview) |
| `scene_setParameter` | Set VRC expression params via Gesture Manager (play mode) |
| `scene_getParameters` | List synced expression parameters with types/defaults |
| `scene_getAnimParams` | Read animator parameter values (live in play, defaults in edit) |
| `scene_setAnimParam` | Set animator parameters (live or default) |

### Scene Hierarchy
| Tool | Description |
|------|-------------|
| `scene_findObjects` | Search by name, component type, tag, or parent |
| `scene_inspectObject` | Full component dump, transform, children, renderer materials |
| `scene_setActive` | Toggle GameObjects active/inactive |

### Editor State
| Tool | Description |
|------|-------------|
| `scene_getState` | Play/pause/compiling mode, scene name, dirty flag, avatar list |
| `scene_searchLogs` | Grep console logs by pattern without changing persistent filter |

## New Feature: Code Execution Handler

Optional `code_execute` tool for running arbitrary C# in the Unity editor via Mono.CSharp evaluator. Not committed to main branches — enable by adding the handler file manually.

## Gesture Manager Integration

`scene_setParameter` integrates with [BlackStartX Gesture Manager](https://github.com/BlackStartx/VRC-Gesture-Manager) for play-mode parameter control:

- Reflects into GM's `ModuleVrc3.Params` dictionary and calls `Vrc3Param.Set(module, value)`
- Switches outfits (`Clothes` parameter), toggles effects, tests expressions
- Requires: play mode + GM window focused (`Tools/Gesture Manager Emulator`)

## Workflows Enabled

### Visual Iteration
```
frameObject → screenshot → orbit → screenshot → analyze → adjust → repeat
```
Eliminates "do you see it?" / "nope" feedback loops. AI can see the Scene View directly.

### Outfit Photo Shoot
```
Enter play mode → Open GM → setParameter(Clothes=N) → screenshot 4 angles → next outfit
```
Automated multi-outfit, multi-angle photography from MCP.

### Play Mode Toggle Testing
```
Enter play mode → Open GM → setParameter(AIEyeGlow=true) → screenshot → compare
```
Test VRC expression toggles with visual verification.

### Edit Mode Toggle Testing
```
listToggles → setDefault(parameter="AIEyeGlow", value=true) → screenshot
```
Preview toggle defaults without entering play mode.

### Recompile from MCP
```
menu_execute("Assets/Refresh") → wait 15s → reconnected
```
Trigger Unity recompile after editing C# files from macOS on shared drive.

## Branches

| Branch | Status | Description |
|--------|--------|-------------|
| `develop` | Active | All fixes + features merged |
| `main` | Synced with upstream | Upstream releases |
| `fix/server-stability` | Merged | #1, #2, #9, #10 |
| `fix/client-reliability` | Merged | #3, #4, #5, #8 |
| `fix/thread-safety` | Merged | #6, #7, #13 |
| `fix/crash-recovery-and-empty-schema` | Merged | #11 |
| `fix/tool-registration` | Merged | #12, #19 |
| `fix/client-registration` | Merged | #14 |
| `fix/tcp-framing` | Merged | #15 |
| `fix/reflection-safety` | Merged | #16 |
| `fix/handler-execution` | Merged | #17 |
| `fix/settings-serialization` | Merged | #18 |
| `fix/start-callback` | Pushed | #21 |
| `fix/settings-null-safety` | Pushed | #22 |
| `fix/server-robustness` | Pushed | #23 (5 bugs) |
| `fix/menu-item-detection` | Pushed | #24 |
| `feature/scene-tools` | Pushed | 18 scene tools + GM integration |

## Stats

- **77 commits** on develop (vs upstream's initial release)
- **24 issues** filed and fixed
- **18 new tools** added
- **~2,600 lines** of new C# + TypeScript code
