# Bubuphluffypaws Fork — Changes & Features

Fork of [isuzu-shiranui/UnityMCP](https://github.com/isuzu-shiranui/UnityMCP) with bug fixes, new scene tools, and VRChat workflow integration.

## Bug Fixes

| Issue | Description | Branch |
|-------|-------------|--------|
| #11 | Duplicate McpServer registration causes handler-less connection | fix/crash-recovery-and-empty-schema |
| #12 | console_getCount and console_clear return "cb is not a function" | fix/tool-registration |
| #21 | Start() doesn't re-register EditorApplication.update after Stop() | fix/start-callback |
| #22 | Settings UI crashes after domain reload (null styles) | fix/settings-null-safety |
| #23 | Server robustness: missing error ID, thread safety, timeout, handle leak, buffer overflow | fix/server-robustness |
| #24 | menu_execute fails for built-in Unity menu items (Assets/Refresh, etc.) | fix/menu-item-detection |

### Deep Code Review Fixes (#23)
- **Error response missing request ID** — TS side hangs 30s on unmatched requests
- **ManualResetEvent handle leak** — race condition on timeout path
- **code_execute timeout** — bumped to 30s (was 5s) for complex operations
- **ProcessIncomingData thread safety** — race with Stop() nulling the client
- **Unbounded incompleteData growth** — capped at 1MB

## New Features: Scene Tools (feature/scene-tools)

18 new MCP tools for iterative Unity/VRChat development. Both C# handler and TypeScript bridge.

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
| `scene_inspectMaterial` | All shader properties as structured JSON |
| `scene_previewTexture` | Export any texture to viewable PNG |

### VRChat Toggle Testing
| Tool | Description |
|------|-------------|
| `scene_listToggles` | List all MA MenuItems with isDefault/param state |
| `scene_setDefault` | Set isDefault on MA toggles (edit mode) |
| `scene_setParameter` | Set VRC params via Gesture Manager (play mode) |
| `scene_getParameters` | List synced expression parameters |
| `scene_getAnimParams` | Read animator parameter values (live or default) |
| `scene_setAnimParam` | Set animator parameters |

### Scene Hierarchy
| Tool | Description |
|------|-------------|
| `scene_findObjects` | Search by name, component, tag, parent |
| `scene_inspectObject` | Full component dump, transform, children |
| `scene_setActive` | Toggle GameObjects on/off |

### Editor State
| Tool | Description |
|------|-------------|
| `scene_getState` | Play/pause/compiling mode, scene info, avatars |
| `scene_searchLogs` | Grep console logs by pattern |

## Gesture Manager Integration

`scene_setParameter` integrates with [BlackStartX Gesture Manager](https://github.com/BlackStartx/VRC-Gesture-Manager) for play-mode parameter control:

- Switches VRC expression parameters via GM's `Vrc3Param.Set()` API
- Enables outfit switching, toggle testing, and visual iteration without leaving the IDE
- Requires play mode + GM window open

## Workflows Enabled

### Visual Iteration (no more "do you see it?" loops)
```
frameObject → screenshot → orbit → screenshot → analyze → adjust → repeat
```

### Outfit Photo Shoot
```
Enter play mode → Open GM → setParameter(Clothes=N) → screenshot 4 angles → next outfit
```

### Toggle Testing
```
Edit mode: listToggles → setDefault → screenshot
Play mode: setParameter → screenshot → compare with AI off
```

## Branches

| Branch | Status | Description |
|--------|--------|-------------|
| `develop` | Active | All fixes + features merged |
| `fix/start-callback` | Pushed | #21 fix |
| `fix/settings-null-safety` | Pushed | #22 fix |
| `fix/server-robustness` | Pushed | #23 fixes (5 bugs) |
| `fix/menu-item-detection` | Pushed | #24 fix |
| `feature/scene-tools` | Pushed | All 18 scene tools |
