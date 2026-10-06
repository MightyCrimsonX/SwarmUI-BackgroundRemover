"""ComfyUI custom nodes for SwarmUI Background Remover."""

from .BackgroundRemoverNode import CharacterBackgroundRemover

NODE_CLASS_MAPPINGS = {
    "CharacterBackgroundRemover": CharacterBackgroundRemover,
    "SwarmBackgroundRemover": CharacterBackgroundRemover,
}

NODE_DISPLAY_NAME_MAPPINGS = {
    "CharacterBackgroundRemover": "Character Background Remover (YOLO / Matting)",
    "SwarmBackgroundRemover": "Swarm Background Remover (YOLO / Matting)",
}

__all__ = ["NODE_CLASS_MAPPINGS", "NODE_DISPLAY_NAME_MAPPINGS"]
