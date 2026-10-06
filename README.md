# SwarmUI Background Remover (SwarmUI-BackgroundRemover)

A high-performance SwarmUI extension designed to automatically detect, isolate, and extract character and human subjects from generated or uploaded images while cleanly stripping away background scenery.

Powered by Ultralytics YOLO instance segmentation and specialized salient matting networks (ISNet-Anime, U2Net-Human, and BiRefNet).

---

## Overview

Standard background removal tools attempt to isolate any salient foreground object indiscriminately. In complex generative AI compositions—such as characters interacting with furniture, holding objects, or standing in intricate environments—general background removers frequently retain unwanted clutter or cut into delicate character silhouettes.

**SwarmUI-BackgroundRemover** specifically focuses on human and character segmentation:
- **Class-Specific Semantic Isolation**: Leverages COCO class 0 (`person`) to isolate humanoid subjects exclusively, automatically ignoring background structures, vehicles, props, and extraneous scenery.
- **Multiple Segmentation Engines**: Choose between YOLO person detection, alpha-refined edge matting, anime-optimized models (`isnet-anime`), or portrait-specific models (`u2net_human_seg`).
- **Subject Filtering**: Isolate all detected characters, or selectively extract only the primary (center-most) or largest character while ignoring distant background figures.
- **Fine Edge Control**: Built-in morphological operations, dilation/erosion padding, and Gaussian feathering ensure smooth, anti-aliased hair strands and clothing boundaries.
- **Native Generation Workflow Integration**: Injects seamlessly into SwarmUI's `WorkflowGenerator` pipeline to process images before final storage and output streaming.
- **Interactive UI & REST API**: Includes dedicated controls in the left generation panel, interactive media action buttons in the image viewer, and a headless API endpoint.

---

## Features

- **Left-Panel Parameter Group**: Located in the SwarmUI left sidebar alongside Resolution, Sampling, and Steps. Enable via the group toggle header to apply automatic character extraction to generations.
- **Multiple Engine Architectures**:
  - `YOLOv8 Person Detection`: Fast, robust instance segmentation isolating the `person` class.
  - `YOLO + Edge Matting`: Combines YOLO person detection with high-resolution edge softening.
  - `ISNet Anime`: Specialized neural model for 2D illustrations and anime character art.
  - `U2Net Human`: Specialized for photographic human portraits.
  - `BiRefNet`: High-precision bilateral reference network for fine silhouette edges.
  - `Auto-Adaptive`: Attempts YOLO person segmentation and automatically falls back to illustration matting if confidence falls below threshold.
- **Configurable Output Backgrounds**:
  - Transparent (RGBA PNG)
  - Solid White (`#FFFFFF`)
  - Solid Black (`#000000`)
  - Green Screen (`#00FF00`)
  - Custom Hex Color
  - Grayscale Alpha Mask Only
- **Multi-Person Management**: Choose whether to keep all detected people, only the largest subject, or the primary center-most subject.
- **Hole Filling & Edge Refinement**: Morphological closing fills interior gaps inside clothing or hair to prevent accidental transparency holes.
- **Viewer Integration**: Injects a one-click action button into SwarmUI's image viewer and history browser for instant post-processing on any image.

---

## Installation

### Prerequisites
SwarmUI includes Python and the ComfyUI backend. Ensure the following dependencies are present in your backend Python environment (auto-satisfied by standard SwarmUI installations):

- `ultralytics`
- `rembg`
- `onnxruntime`
- `opencv-python-headless`
- `torch` and `torchvision`

### Installation Steps

1. Clone or copy the `SwarmUI-BackgroundRemover` folder into SwarmUI's extensions directory:
   ```bash
   git clone <repo-url> src/Extensions/SwarmUI-BackgroundRemover
   ```
2. Launch SwarmUI via `launch-windows.bat` (Windows) or `launch-linux.sh` (Linux).
3. SwarmUI will automatically detect the `.csproj` file, build the C# extension assembly, load the ComfyUI custom node, and register frontend assets.

---

## Parameter Reference

When the **Character Background Removal** parameter group is enabled in the left sidebar, the following parameters are available:

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Engine` | Dropdown | `yolov8_person` | Segmentation backend (`yolov8_person`, `yolo_refined`, `isnet_anime`, `u2net_human`, `birefnet`, `auto`). |
| `YOLO Model` | Dropdown | `yolov8m-seg.pt` | Model checkpoint (`yolov8n-seg.pt`, `yolov8s-seg.pt`, `yolov8m-seg.pt`, `yolov8l-seg.pt`, `yolov8x-seg.pt`, or YOLO11 variants). |
| `Confidence Threshold` | Slider | `0.30` | Minimum confidence score (0.01 to 1.0) required to recognize a character instance. |
| `Subject Selection` | Dropdown | `all` | Strategy for handling multiple detected subjects: `all`, `largest`, `primary`, or `highest_conf`. |
| `Mask Edge Padding` | Slider | `0` | Expands (> 0) or contracts (< 0) the mask contour in pixels (-50 to +50). |
| `Mask Feathering Blur` | Slider | `2` | Gaussian blur radius in pixels applied to the alpha mask for smooth hair and edge blending. |
| `Output Background` | Dropdown | `transparent` | Output styling: `transparent`, `white`, `black`, `greenscreen`, `custom`, or `mask_only`. |
| `Custom Background Color` | String | `#FFFFFF` | Hex color code applied when `custom` output mode is chosen. |
| `Fill Interior Holes` | Boolean | `true` | Fills enclosed contours to avoid transparent gaps within clothes, skin, or hair. |
| `Invert Mask` | Boolean | `false` | Inverts segmentation logic to remove the character and keep the background. |
| `Save Original Before Removal` | Boolean | `false` | Retains an intermediate copy of the raw generated image prior to background extraction. |
| `Input Image` | Image | `None` | Interactive input field to select an existing image for standalone character extraction. |

---

## API Documentation

### Route: `/API/RemoveCharacterBackground`

Processes an arbitrary image and returns the isolated character subject.

- **HTTP Method**: `POST`
- **Session Required**: Yes
- **Permission**: `character_rembg_process`

#### Request Payload (JSON)

```json
{
  "imageBase64": "<base64_encoded_image_data>",
  "engine": "yolov8_person",
  "yoloModel": "yolov8m-seg.pt",
  "confidence": 0.30,
  "personSelection": "all",
  "maskPadding": 0,
  "maskBlur": 2,
  "backgroundColor": "transparent",
  "customColor": "#FFFFFF",
  "invertMask": false,
  "fillHoles": true
}
```

#### Response Format (JSON)

```json
{
  "success": true,
  "image": "<base64_encoded_png_with_alpha>",
  "image_type": "image/png"
}
```

---

## Technical Architecture

1. **Extension Entry Point (`CharacterRemBgExtension.cs`)**:
   - Registers parameters within `T2IParamGroup` (Order Priority `82`).
   - Hooks a generation step into `WorkflowGenerator.AddStep(..., priority: 52)`.
   - Converts the incoming media stream to raw image representation (`g.CurrentMedia.AsRawImage()`).
   - Inserts the `CharacterBackgroundRemover` ComfyUI node and updates `g.CurrentMedia` with `mayHaveAlpha: true`.
2. **ComfyUI Custom Node (`CharacterRemBgNode.py`)**:
   - Caches loaded YOLO and matting models in memory for sub-second repeat executions.
   - Evaluates inputs in PyTorch inference mode without gradient computation.
   - Extracts character masks and applies OpenCV-accelerated morphological operations.
   - Composites alpha channels or solid colors, returning both `IMAGE` and `MASK` tensors.
3. **Frontend Integration (`character_rembg.js` & `character_rembg.css`)**:
   - Registers the media button in SwarmUI's output viewer via `registerMediaButton`.
   - Mounts the interactive action button into the `Input Image` parameter element via `postParamBuildSteps`.
   - Displays comparison modals with dark-theme checkerboard transparency preview.

---

## License

This extension is licensed under the MIT License.
