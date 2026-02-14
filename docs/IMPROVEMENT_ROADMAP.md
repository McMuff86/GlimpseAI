# GlimpseAI Improvement Roadmap
**Analysis Date:** February 14, 2026  
**Analyst:** AI Software Architect  
**Scope:** Complete codebase analysis and feature gap assessment

---

## Executive Summary

GlimpseAI is a sophisticated Rhino plugin with solid core architecture and impressive technical implementation. However, several critical issues prevent it from being production-ready, and many essential UX features are missing. This roadmap prioritizes fixes and improvements for maximum user impact.

**Current State:** 
- ✅ Core AI rendering pipeline works
- ✅ Multi-preset system (Fast/Balanced/HQ/4K)  
- ✅ WebSocket + HTTP fallback architecture
- ✅ Thread-safety fixes implemented
- ⚠️ Still has crash-causing issues
- ❌ Missing essential UX features

---

## P0 - CRITICAL (Must Fix Before Release)

### 1. **Error UX - User sees nothing when things fail** 🚨
**Current State:** Users only see generic errors in Rhino command line
**Impact:** Plugin appears broken, users can't troubleshoot

**Missing:**
- [ ] **Error Dialog System** - Modal dialogs for critical errors with actionable advice
- [ ] **In-Panel Error Display** - Red status bar in main panel showing current issue
- [ ] **ComfyUI Offline Handling** - Clear "Start ComfyUI Server" guidance with setup links  
- [ ] **Model Missing Detection** - Scan for required models, show install instructions
- [ ] **VRAM Error Handling** - Detect CUDA OOM, suggest resolution reduction
- [ ] **Network Error Recovery** - "Retry Connection" button, offline mode indicator

**Implementation Priority:** P0 - Users abandon plugins that "don't work"

### 2. **Thread-Safety Crashes** 🚨  
**Current State:** Multiple race conditions in `GlimpseOverlayConduit` and `ViewportCapture`
**Impact:** Random Rhino crashes, data loss

**Critical Issues Found:**
```csharp
// CRASH: DisplayBitmap disposal during viewport rendering
protected override void DrawForeground(DrawEventArgs e)
{
    lock (_lock) {
        if (_displayBitmap == null) return; // ❌ Can become null here
        e.Display.DrawSprite(_displayBitmap, ...); // ❌ Crash!
    }
}

// CRASH: DisplayMode switching during active render
viewport.DisplayMode = arcticMode; // ❌ No render sync
view.Redraw(); // ❌ Race condition
```

**Required Fixes:**
- [ ] **Proper Double-Buffering** in `GlimpseOverlayConduit` 
- [ ] **Render Synchronization** in `ViewportCapture.CaptureDepthApprox()`
- [ ] **UI Thread Marshalling** for all Rhino API calls
- [ ] **WebSocket Buffer Management** - dynamic allocation for large preview images

**Implementation Priority:** P0 - Crashes kill user trust immediately

### 3. **Memory Management Issues** ⚠️
**Current State:** DisplayBitmap recreation on every update, no cleanup
**Impact:** Memory leaks, performance degradation, eventual crashes

**Issues:**
- [ ] **DisplayBitmap Pooling** - Reuse instead of constant recreation
- [ ] **Large Image Limits** - Prevent >50MB allocations, add warnings
- [ ] **GC Pressure Relief** - Explicit disposal, memory pressure monitoring
- [ ] **WebSocket Buffer Leaks** - Proper ArrayPool usage in ComfyUIClient

**Implementation Priority:** P0 - Memory leaks cause delayed crashes

---

## P1 - ESSENTIAL FEATURES (Missing Must-Haves)

### 1. **Preset Management - Users Can't Create Custom Presets** 
**Current State:** Hardcoded 4 presets, no user customization
**User Expectation:** Like Photoshop actions - save, share, import presets

**Missing Features:**
- [ ] **Save Current Settings as Preset** - "Save as..." button in panel
- [ ] **Custom Preset Editor** - Modal dialog with all parameters
- [ ] **Preset Library Management** - Import/export, rename, delete  
- [ ] **Preset Sharing** - JSON export format for community sharing
- [ ] **Workflow Template System** - Advanced users can edit ComfyUI workflows
- [ ] **Preset Categories** - Architecture, Interior, Landscape, etc.

**Implementation:** 
```csharp
public class CustomPreset 
{
    public string Name { get; set; }
    public PresetType BaseType { get; set; }
    public string WorkflowJson { get; set; } // ComfyUI workflow
    public Dictionary<string, object> Parameters { get; set; }
}
```

### 2. **Generation History - No Way to See Previous Results**
**Current State:** Only current generation visible, no browsing
**User Expectation:** Like browser history - browse, compare, restore

**Missing Features:**
- [ ] **History Panel** - Thumbnail grid of last 20-50 generations
- [ ] **History Navigation** - Previous/Next buttons, keyboard shortcuts
- [ ] **Generation Metadata** - Timestamp, prompt, preset, seed, model
- [ ] **History Search/Filter** - Find by prompt keywords, preset, date
- [ ] **History Export** - Save entire session as image sequence
- [ ] **Persistent History** - Save across Rhino sessions

**UI Design:**
```
┌─────────────────────────────┐
│ Current Result              │
│ ┌─────────────────────────┐ │
│ │                         │ │
│ │     AI Render           │ │
│ │                         │ │
│ └─────────────────────────┘ │
│ [◄ Prev]  [History]  [Next ►] │
│                             │
│ History: 12:34  modern arch │
│ Preset: HQ  Seed: 42  Model │
└─────────────────────────────┘
```

### 3. **Undo - No Way to Restore Previous Images**
**Current State:** Each generation overwrites the previous
**User Expectation:** Ctrl+Z functionality

**Missing Features:**
- [ ] **Undo/Redo Stack** - Last 10 viewport images with AI results  
- [ ] **Viewport Image Restoration** - Restore original viewport capture
- [ ] **Quick Restore Button** - One-click "Show Original"
- [ ] **Undo History Display** - List of operations that can be undone
- [ ] **Keyboard Shortcuts** - Ctrl+Z, Ctrl+Y support

### 4. **Progress Feedback - Users Don't Know What's Happening**
**Current State:** Basic progress bar, no detailed status
**User Expectation:** Clear feedback during 10-60 second waits

**Missing Features:**
- [ ] **Detailed Progress Steps** - "Uploading...", "Generating...", "Downloading..."
- [ ] **Time Estimation** - "~15 seconds remaining" based on preset
- [ ] **Live Preview Updates** - Show latent preview images during generation
- [ ] **Queue Position Display** - "Position 2 of 3 in ComfyUI queue"
- [ ] **Cancellation with Cleanup** - Stop server-side processing too
- [ ] **Background Progress** - Continue in background while user works

### 5. **Keyboard Shortcuts - Power Users Expect Hotkeys**
**Current State:** No keyboard shortcuts at all  
**User Expectation:** Industry standard hotkeys

**Missing Shortcuts:**
- [ ] **F5** - Generate (universal "refresh" key)
- [ ] **Ctrl+G** - Generate with prompt dialog  
- [ ] **Space** - Toggle Auto mode (quick switch)
- [ ] **Ctrl+S** - Save current result  
- [ ] **Ctrl+E** - Export 4K version
- [ ] **Ctrl+Z/Y** - Undo/Redo
- [ ] **←/→** - Navigate history
- [ ] **Ctrl+T** - Toggle overlay
- [ ] **Tab** - Focus prompt area
- [ ] **Esc** - Cancel generation

---

## P2 - ARCHITECTURAL IMPROVEMENTS (Code Quality)

### 1. **Error Handling - Many Unhandled Edge Cases**
**Issues Found:**
```csharp
// Missing try/catch in critical paths
var result = await _comfyClient.GenerateAsync(request, ct); // ❌ Can throw
_overlayConduit.UpdateImage(result.ImageData); // ❌ No null check

// Generic error handling
catch (Exception ex) { 
    return RenderResult.Fail(ex.Message, elapsed); // ❌ Not actionable
}
```

**Required Improvements:**
- [ ] **Specific Exception Types** - `ComfyUIOfflineException`, `ModelMissingException`
- [ ] **Error Recovery Strategies** - Auto-retry with backoff, fallback presets
- [ ] **Error Context Preservation** - Include user action, system state
- [ ] **Error Reporting System** - Anonymous crash reports for debugging

### 2. **Logging - Insufficient for Production Debugging**  
**Current State:** Only `RhinoApp.WriteLine()` for basic info
**Issue:** Users can't troubleshoot, developers can't debug remote issues

**Required Improvements:**
- [ ] **Structured Logging** - JSON format with levels, timestamps
- [ ] **Log File Output** - Persistent logs in `%APPDATA%/GlimpseAI/logs/`
- [ ] **Performance Logging** - Generation times, memory usage, bottlenecks
- [ ] **User Action Logging** - Button clicks, setting changes, workflow
- [ ] **Debug Mode** - Verbose logging toggle in settings
- [ ] **Log Viewer** - Built-in dialog to view recent logs

### 3. **Settings Persistence - Missing Advanced Settings**
**Issues:**
- Some settings not saved (overlay opacity, history preferences)  
- No settings backup/restore
- No per-project settings

**Required Improvements:**
- [ ] **Complete Settings Coverage** - Every user preference saved
- [ ] **Settings Backup** - Export/import for workspace migration  
- [ ] **Per-Document Settings** - Different presets per project
- [ ] **Settings Validation** - Prevent invalid configurations
- [ ] **Settings Migration** - Handle version upgrades gracefully

### 4. **Race Conditions - Concurrent Operation Issues**
**Remaining Issues:**
```csharp
// Multiple rapid viewport changes
_currentGenerationCts?.Cancel(); // ❌ Previous may still be running
_currentGenerationCts = new CancellationTokenSource(); 

// WebSocket reconnection during generation  
await _comfyClient.ConnectWebSocketAsync(ct); // ❌ Race with active generation
```

**Required Fixes:**
- [ ] **Generation Queue Management** - Single active generation at a time
- [ ] **Resource Lock Hierarchy** - Prevent deadlocks in disposal
- [ ] **State Machine Implementation** - Clear states: Idle, Generating, Canceling
- [ ] **Async Coordination** - Proper task scheduling and cleanup

---

## P3 - ADVANCED FEATURES (Nice to Have)

### 1. **Flux Support - Different AI Architecture**
**Current State:** SDXL/SD1.5 focused workflows  
**User Demand:** Flux models produce better architectural renders

**Required Implementation:**
- [ ] **Flux Workflow Templates** - Different node structure than SDXL
- [ ] **Model Type Detection** - Auto-detect Flux vs SDXL checkpoints
- [ ] **Flux-Specific Presets** - Optimized steps, guidance scale
- [ ] **Model Compatibility Warnings** - Prevent incompatible combinations

### 2. **Batch Rendering - Multiple Viewports**
**User Request:** Render all viewports at once for presentation sheets  
**Current Limitation:** One viewport at a time

**Feature Design:**
- [ ] **Viewport Selection Dialog** - Choose which views to render
- [ ] **Batch Queue Management** - Show progress for all renders
- [ ] **Consistent Settings** - Same preset/prompt for all views
- [ ] **Layout Export** - Combine results into presentation layout
- [ ] **Batch Progress Monitoring** - Overall progress + individual status

### 3. **A/B Comparison - Side-by-Side Results**
**User Need:** Compare different presets, prompts, or seeds
**Current Limitation:** Only see one result at a time

**Feature Design:**
```
┌──────────────────────────────────────────┐
│ A/B Comparison Mode                      │
├────────────────────┬─────────────────────┤
│ Result A           │ Result B            │
│ ┌─────────────────┐│ ┌─────────────────┐ │
│ │                 ││ │                 │ │
│ │   HQ Preset     ││ │   Fast Preset   │ │
│ │   30s           ││ │   2s            │ │
│ └─────────────────┘│ └─────────────────┘ │
├────────────────────┼─────────────────────┤
│ Prompt: modern...  │ Prompt: artistic... │
│ [Generate A]       │ [Generate B]        │
└────────────────────┴─────────────────────┘
```

### 4. **Render Queue - Multiple Sequential Jobs**  
**User Scenario:** Queue up multiple different prompts/presets while working
**Current Limitation:** Must wait for each generation to complete

**Feature Design:**
- [ ] **Queue Management Panel** - Add, remove, reorder jobs
- [ ] **Queue Persistence** - Save queue across Rhino sessions  
- [ ] **Priority System** - High priority jobs jump queue
- [ ] **Background Processing** - Queue runs while user continues modeling
- [ ] **Queue Results History** - All queued results saved automatically

### 5. **Mask/Inpainting - Selective Region Rendering**
**Advanced Feature:** Only render specific parts of the viewport
**User Benefit:** Faster iterations, preserve existing good parts

**Implementation Complexity:** High - requires selection UI, mask generation
**Market Differentiation:** Few competing tools have this

### 6. **LoRA Support - Fine-Tuned Model Variants**  
**User Request:** Use specialized architectural LoRA models
**Technical Challenge:** ComfyUI LoRA integration in workflows

### 7. **IP-Adapter - Style Reference Images**
**Advanced Feature:** Use reference images to guide style/composition
**User Workflow:** "Make it look like this building photo"  
**Implementation:** Requires additional ComfyUI nodes and UI for image selection

---

## UX IMPROVEMENTS

### 1. **Panel Layout Issues**
**Current Problems:**
- Progress bar only appears during generation (confusing)
- Status text too small and gray (hard to read)
- No visual feedback for settings changes
- Overlay controls separated from preview area

**Improvements:**
- [ ] **Always-Visible Status Bar** - Show connection, last generation time
- [ ] **Larger Status Text** - Readable fonts, better contrast
- [ ] **Setting Change Feedback** - Visual confirmation of changes  
- [ ] **Logical Control Grouping** - Related controls near each other
- [ ] **Responsive Layout** - Adapt to panel width changes

### 2. **Missing Important Controls**
**Critical Gaps:**
- [ ] **Quick Preview Toggle** - Fast preview of changes without full generation
- [ ] **Last Generation Info** - Show metadata of current result
- [ ] **Model Information Display** - Which AI model is loaded/selected
- [ ] **VRAM Usage Indicator** - Warn before memory issues
- [ ] **Batch Size Control** - For users with limited VRAM

### 3. **Workflow Intuitiveness**  
**Current Confusion Points:**
- Auto-prompt mode not clearly explained
- ControlNet settings buried in advanced dialog
- No guidance on which preset to use when

**Improvements:**
- [ ] **Onboarding Tips** - First-time user guidance bubbles
- [ ] **Preset Recommendations** - Suggest preset based on scene complexity
- [ ] **Mode Explanations** - Tooltip explanations for all modes
- [ ] **Visual Workflow Hints** - Icons showing what each step does
- [ ] **Smart Defaults** - Better default settings for new users

### 4. **What Architects/Designers Expect**
**Based on Industry Tools:**
- [ ] **Material Override Rendering** - Apply different materials quickly
- [ ] **Lighting Scenario Testing** - Day/night/golden hour presets
- [ ] **View Animation** - Generate sequence of camera positions  
- [ ] **Style Library** - Predefined architectural styles (modern, classical, etc.)
- [ ] **Export Integration** - Direct export to presentation tools
- [ ] **Print-Ready Output** - High DPI settings for drawings

---

## COMPETITIVE ANALYSIS

### Veras (Revit AI Render) - What They Do Better

**Strengths to Learn From:**
1. **Onboarding Excellence** - Step-by-step first-run setup  
2. **Error Prevention** - Warn before problems (low VRAM, missing models)
3. **Style Consistency** - Maintain architectural style across generations
4. **Export Workflow** - Direct integration with presentation tools
5. **Business Model** - Clear pricing, pro features vs. free

**GlimpseAI Advantages:**
- ✅ Local processing (no subscription costs)
- ✅ Pixel-perfect depth maps (vs. estimated)
- ✅ Real-time preview capability  
- ✅ Open source ComfyUI ecosystem
- ✅ Full workflow customization

### Stable Diffusion Web UI - What They Do Better

**Strengths to Learn From:**
1. **Extensive History** - Image browser with metadata
2. **Prompt Engineering** - Autocomplete, templates, weighting
3. **Model Management** - Easy model switching, VRAM monitoring
4. **Batch Processing** - Queue system, variations, comparisons
5. **Extension Ecosystem** - Plugin architecture for add-ons

**GlimpseAI Advantages:**
- ✅ CAD integration (vs. standalone tool)
- ✅ Viewport-aware generation
- ✅ Architecture-specific features
- ✅ Real-time overlay capability

### GlimpseAI's Unique Positioning

**Competitive Moat:**
1. **Rhino Native Integration** - Only tool with deep CAD integration  
2. **Architect-Focused UX** - Designed for architectural workflow
3. **Real-time Viewport Overlay** - See results in context  
4. **Precise Depth Control** - Use actual CAD geometry depth
5. **Material-Aware Prompting** - Auto-detect scene materials

**Threat Analysis:**
- **Autodesk** could build similar into Revit/AutoCAD
- **McNeel** could build AI rendering into Rhino directly  
- **Open source alternatives** might emerge

**Strategic Response:**
- Focus on architectural workflow optimization
- Build strong community and preset library
- Maintain technical lead in CAD integration depth

---

## IMPLEMENTATION PRIORITY MATRIX

### Phase 1: Critical Stability (2-3 weeks)
**Goal:** Production-ready reliability

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| Error UX System | High | Medium | P0 |
| Thread-Safety Fixes | High | High | P0 |
| Memory Management | High | Medium | P0 |
| Basic Error Dialogs | High | Low | P0 |
| ComfyUI Offline Handling | High | Low | P0 |

### Phase 2: Essential UX (3-4 weeks)  
**Goal:** Feature-complete for power users

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| Generation History | High | Medium | P1 |
| Keyboard Shortcuts | Medium | Low | P1 |
| Undo/Redo Stack | High | Medium | P1 |
| Progress Improvements | Medium | Low | P1 |
| Custom Preset System | High | High | P1 |

### Phase 3: Advanced Features (4-6 weeks)
**Goal:** Market differentiation

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| A/B Comparison | Medium | Medium | P3 |
| Batch Rendering | Medium | High | P3 |
| Render Queue | Medium | Medium | P3 |
| Flux Support | High | High | P3 |
| Mask/Inpainting | Low | Very High | P3 |

### Phase 4: Polish & Distribution (2-3 weeks)
**Goal:** Professional product launch

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| Onboarding System | Medium | Medium | P2 |
| Documentation | High | Medium | P2 |
| Installer Package | High | Low | P2 |
| Website & Marketing | High | Medium | P2 |
| Food4Rhino Submission | High | Low | P2 |

---

## RISK ASSESSMENT

### Technical Risks

**High Risk:**
- **ComfyUI API Changes** - Upstream changes could break workflows
  - *Mitigation:* Version pinning, API abstraction layer
- **Rhino SDK Changes** - Breaking changes in future Rhino versions  
  - *Mitigation:* Minimal API surface, compatibility testing

**Medium Risk:**
- **Performance with Large Models** - GPU memory limitations
  - *Mitigation:* Dynamic resolution scaling, VRAM monitoring
- **WebSocket Reliability** - Network issues affecting real-time updates
  - *Mitigation:* Robust HTTP fallback, connection retry logic

### Market Risks  

**High Risk:**
- **Competitive Response** - Autodesk/McNeel building integrated AI
  - *Mitigation:* Focus on specialized architectural workflow
- **AI Model Licensing** - Commercial use restrictions
  - *Mitigation:* Support multiple model providers

**Low Risk:**
- **User Adoption** - Architects slow to adopt new tools
  - *Mitigation:* Free tier, extensive documentation, demos

---

## SUCCESS METRICS

### Technical KPIs
- **Crash Rate:** <0.1% of sessions  
- **Generation Success Rate:** >95%
- **Average Generation Time:** <30s for HQ preset
- **Memory Usage:** <2GB peak during normal operation
- **User Error Rate:** <5% fail to generate first result

### User Experience KPIs  
- **Time to First Success:** <5 minutes from install
- **Daily Active Usage:** >10 generations per active user
- **Feature Discovery:** >80% find history/shortcuts within first week
- **User Retention:** >70% return after first successful use

### Business KPIs
- **Food4Rhino Downloads:** Target 1000+ in first 3 months
- **Community Engagement:** Active Discord/forum discussion  
- **Preset Sharing:** User-generated preset library growth
- **Pro Conversion:** If freemium model, >10% upgrade rate

---

## CONCLUSION

GlimpseAI has excellent technical foundations but needs significant UX investment to become a production-ready tool. The core AI integration works well, but missing essential features (error handling, history, undo) and remaining stability issues prevent mainstream adoption.

**Critical Path to Success:**
1. **Fix crash-causing bugs** - Users won't tolerate instability
2. **Build essential UX features** - History, presets, shortcuts  
3. **Polish error handling** - Users need clear feedback
4. **Create compelling demos** - Show architectural use cases

**Unique Market Position:**
GlimpseAI's deep Rhino integration and architecture-focused design provide strong competitive advantages. Success depends on execution quality and addressing user workflow needs, not just technical capabilities.

**Resource Allocation Recommendation:**
- 60% stability and core UX (P0/P1 items)
- 30% advanced features and differentiation (P3 items)  
- 10% polish and marketing (documentation, packaging)

The project has strong potential for success in the architectural visualization market with focused execution on user-facing improvements.