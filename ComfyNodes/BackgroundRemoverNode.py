"""ComfyUI custom node for Character Background Remover using YOLO segmentation and specialized matting."""

import os
import re
import cv2
import numpy as np
from PIL import Image
import torch

_YOLO_MODELS = {}
_REMBG_SESSIONS = {}

COLOR_MAP = {
    "white": (1.0, 1.0, 1.0),
    "black": (0.0, 0.0, 0.0),
    "greenscreen": (0.0, 1.0, 0.0),
    "transparent": None
}


def parse_hex_color(hex_str: str) -> tuple:
    """Parses a hex color string (e.g., #FFFFFF or #00FF00) into normalized RGB float tuple."""
    if not hex_str:
        return (1.0, 1.0, 1.0)
    cleaned = hex_str.strip().lstrip("#")
    if len(cleaned) == 3:
        cleaned = "".join([c * 2 for c in cleaned])
    if len(cleaned) != 6:
        return (1.0, 1.0, 1.0)
    try:
        r = int(cleaned[0:2], 16) / 255.0
        g = int(cleaned[2:4], 16) / 255.0
        b = int(cleaned[4:6], 16) / 255.0
        return (r, g, b)
    except Exception:
        return (1.0, 1.0, 1.0)


def get_yolo_model(model_name: str):
    """Loads and caches the requested Ultralytics YOLO segmentation model."""
    global _YOLO_MODELS
    if model_name not in _YOLO_MODELS:
        from ultralytics import YOLO
        print(f"[CharacterRemBg] Loading YOLO segmentation model: {model_name}...")
        _YOLO_MODELS[model_name] = YOLO(model_name)
    return _YOLO_MODELS[model_name]


def get_rembg_session(model_name: str):
    """Loads and caches the requested rembg session."""
    global _REMBG_SESSIONS
    if model_name not in _REMBG_SESSIONS:
        from rembg import new_session
        print(f"[CharacterRemBg] Initializing rembg session: {model_name}...")
        _REMBG_SESSIONS[model_name] = new_session(model_name)
    return _REMBG_SESSIONS[model_name]


class CharacterBackgroundRemover:
    @classmethod
    def INPUT_TYPES(cls):
        return {
            "required": {
                "images": ("IMAGE",),
                "engine": (
                    [
                        "yolov8_person",
                        "yolo_refined",
                        "isnet_anime",
                        "u2net_human",
                        "birefnet",
                        "auto"
                    ],
                    {"default": "yolov8_person"}
                ),
                "yolo_model": (
                    [
                        "yolov8m-seg.pt",
                        "yolov8s-seg.pt",
                        "yolov8n-seg.pt",
                        "yolov8l-seg.pt",
                        "yolov8x-seg.pt",
                        "yolo11n-seg.pt",
                        "yolo11m-seg.pt",
                        "yolo11x-seg.pt"
                    ],
                    {"default": "yolov8m-seg.pt"}
                ),
                "confidence": ("FLOAT", {"default": 0.30, "min": 0.01, "max": 1.0, "step": 0.01}),
                "person_selection": (["all", "largest", "primary", "highest_conf"], {"default": "all"}),
                "mask_padding": ("INT", {"default": 0, "min": -50, "max": 50, "step": 1}),
                "mask_blur": ("INT", {"default": 2, "min": 0, "max": 30, "step": 1}),
                "background_color": (["transparent", "white", "black", "greenscreen", "custom", "mask_only"], {"default": "transparent"}),
                "custom_color": ("STRING", {"default": "#FFFFFF"}),
                "invert_mask": ("BOOLEAN", {"default": False}),
                "fill_holes": ("BOOLEAN", {"default": True}),
            }
        }

    CATEGORY = "SwarmUI/images"
    RETURN_TYPES = ("IMAGE", "MASK")
    RETURN_NAMES = ("image", "mask")
    FUNCTION = "remove_background"

    def remove_background(
        self,
        images,
        engine="yolov8_person",
        yolo_model="yolov8m-seg.pt",
        confidence=0.30,
        person_selection="all",
        mask_padding=0,
        mask_blur=2,
        background_color="transparent",
        custom_color="#FFFFFF",
        invert_mask=False,
        fill_holes=True
    ):
        output_images = []
        output_masks = []

        for image in images:
            # ComfyUI Image tensor is [H, W, C] in range [0, 1]
            img_np = (255.0 * image.cpu().numpy()).clip(0, 255).astype(np.uint8)
            orig_h, orig_w = img_np.shape[:2]

            # Ensure RGB
            if img_np.shape[-1] == 4:
                rgb_img = cv2.cvtColor(img_np, cv2.COLOR_RGBA2RGB)
            else:
                rgb_img = img_np

            pil_img = Image.fromarray(rgb_img)

            raw_mask = None
            detected_any = False

            # Execute YOLO segmentation if selected or auto
            if engine in ["yolov8_person", "yolo_refined", "auto"]:
                try:
                    model = get_yolo_model(yolo_model)
                    with torch.inference_mode():
                        # Class 0 in COCO is person
                        results = model.predict(source=pil_img, classes=[0], conf=confidence, verbose=False)

                    if results and len(results) > 0 and results[0].masks is not None and len(results[0].masks.data) > 0:
                        detected_any = True
                        res = results[0]
                        masks_data = res.masks.data.cpu().numpy()
                        boxes = res.boxes

                        person_masks = []
                        for m in masks_data:
                            # Resize mask to original image dimensions if needed
                            if m.shape[0] != orig_h or m.shape[1] != orig_w:
                                m_resized = cv2.resize(m.astype(np.float32), (orig_w, orig_h), interpolation=cv2.INTER_LINEAR)
                            else:
                                m_resized = m.astype(np.float32)
                            person_masks.append(m_resized)

                        if person_selection == "all" or len(person_masks) == 1:
                            raw_mask = np.clip(np.sum(person_masks, axis=0), 0.0, 1.0)
                        elif person_selection == "largest":
                            areas = [np.sum(m > 0.5) for m in person_masks]
                            best_idx = int(np.argmax(areas))
                            raw_mask = person_masks[best_idx]
                        elif person_selection == "primary":
                            center_x, center_y = orig_w / 2.0, orig_h / 2.0
                            scores = []
                            for idx, m in enumerate(person_masks):
                                area = float(np.sum(m > 0.5))
                                if boxes is not None and len(boxes) > idx:
                                    box = boxes[idx].xyxy[0].cpu().numpy()
                                    bx = (box[0] + box[2]) / 2.0
                                    by = (box[1] + box[3]) / 2.0
                                else:
                                    moments = cv2.moments((m > 0.5).astype(np.uint8))
                                    if moments["m00"] > 0:
                                        bx = moments["m10"] / moments["m00"]
                                        by = moments["m01"] / moments["m00"]
                                    else:
                                        bx, by = center_x, center_y
                                dist = np.sqrt((bx - center_x) ** 2 + (by - center_y) ** 2)
                                score = area / (1.0 + dist * 0.5)
                                scores.append(score)
                            best_idx = int(np.argmax(scores))
                            raw_mask = person_masks[best_idx]
                        elif person_selection == "highest_conf":
                            if boxes is not None and len(boxes.conf) > 0:
                                best_idx = int(boxes.conf.argmax().item())
                                raw_mask = person_masks[best_idx]
                            else:
                                raw_mask = person_masks[0]
                    else:
                        print(f"[CharacterRemBg] No character detected with YOLO at confidence {confidence:.2f}.")
                except Exception as ex:
                    print(f"[CharacterRemBg] YOLO execution error: {ex}")

            # Fallback or specialized matting engines
            if raw_mask is None:
                rembg_model_name = "u2net_human_seg"
                if engine == "isnet_anime" or (engine == "auto" and not detected_any):
                    rembg_model_name = "isnet-anime"
                elif engine == "birefnet":
                    rembg_model_name = "birefnet-general"
                elif engine == "u2net_human":
                    rembg_model_name = "u2net_human_seg"

                try:
                    from rembg import remove
                    session = get_rembg_session(rembg_model_name)
                    mask_pil = remove(pil_img, session=session, only_mask=True)
                    raw_mask = (np.array(mask_pil).astype(np.float32) / 255.0).clip(0.0, 1.0)
                except Exception as ex:
                    print(f"[CharacterRemBg] RemBG execution error: {ex}")
                    # If all failed, provide full opacity mask
                    raw_mask = np.ones((orig_h, orig_w), dtype=np.float32)

            # Convert mask to uint8 for morphological operations
            mask_uint8 = (raw_mask * 255.0).clip(0, 255).astype(np.uint8)

            # Fill interior holes if enabled
            if fill_holes:
                kernel_close = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))
                mask_uint8 = cv2.morphologyEx(mask_uint8, cv2.MORPH_CLOSE, kernel_close)
                contours, _ = cv2.findContours(mask_uint8, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
                for cnt in contours:
                    cv2.drawContours(mask_uint8, [cnt], 0, 255, -1)

            # Apply mask padding (dilate or erode)
            if mask_padding != 0:
                k_size = abs(mask_padding) * 2 + 1
                kernel_pad = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k_size, k_size))
                if mask_padding > 0:
                    mask_uint8 = cv2.dilate(mask_uint8, kernel_pad, iterations=1)
                else:
                    mask_uint8 = cv2.erode(mask_uint8, kernel_pad, iterations=1)

            # Edge feathering / Gaussian blur
            if mask_blur > 0:
                k_blur = mask_blur * 2 + 1
                mask_uint8 = cv2.GaussianBlur(mask_uint8, (k_blur, k_blur), 0)

            # Convert back to float [0, 1]
            final_mask_float = mask_uint8.astype(np.float32) / 255.0

            if invert_mask:
                final_mask_float = 1.0 - final_mask_float

            # Alpha mask tensor for ComfyUI: ComfyUI standard mask is 1.0 = masked / inverted = 0.0
            # For SwarmUI/ComfyUI, mask return value is [H, W] float
            comfy_mask = 1.0 - final_mask_float

            # Prepare output image
            img_rgb_float = rgb_img.astype(np.float32) / 255.0

            if background_color == "transparent":
                # Create RGBA float image [H, W, 4]
                alpha_channel = np.expand_dims(final_mask_float, axis=-1)
                out_img = np.concatenate([img_rgb_float, alpha_channel], axis=-1)
            elif background_color == "mask_only":
                # Output 3-channel grayscale representation of the mask
                mask_3ch = np.repeat(np.expand_dims(final_mask_float, axis=-1), 3, axis=-1)
                out_img = mask_3ch
            else:
                # Solid color background
                bg_color = COLOR_MAP.get(background_color)
                if bg_color is None:
                    bg_color = parse_hex_color(custom_color)
                bg_arr = np.array(bg_color, dtype=np.float32).reshape(1, 1, 3)
                alpha_3ch = np.repeat(np.expand_dims(final_mask_float, axis=-1), 3, axis=-1)
                out_img = img_rgb_float * alpha_3ch + bg_arr * (1.0 - alpha_3ch)

            output_images.append(out_img)
            output_masks.append(comfy_mask)

        return (
            torch.from_numpy(np.array(output_images, dtype=np.float32)),
            torch.from_numpy(np.array(output_masks, dtype=np.float32))
        )
