using System;
using System.IO;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace SwarmUI.BackgroundRemover;

/// <summary>SwarmUI Extension for isolating characters and removing backgrounds from generated images using YOLO segmentation and matting.</summary>
public class BackgroundRemoverExtension : Extension
{
    /// <summary>Absolute path to this extension's directory.</summary>
    public static string ExtFolder;

    /// <summary>Parameter group for Character Background Removal controls.</summary>
    public static T2IParamGroup BackgroundRemoverGroup;

    /// <summary>Primary segmentation engine selector.</summary>
    public static T2IRegisteredParam<string> EngineParam;

    /// <summary>YOLO segmentation model checkpoint.</summary>
    public static T2IRegisteredParam<string> YoloModelParam;

    /// <summary>Minimum detection confidence threshold.</summary>
    public static T2IRegisteredParam<double> ConfidenceParam;

    /// <summary>Strategy for selecting which character(s) to retain.</summary>
    public static T2IRegisteredParam<string> PersonSelectionParam;

    /// <summary>Mask edge dilation (positive) or erosion (negative) in pixels.</summary>
    public static T2IRegisteredParam<int> MaskPaddingParam;

    /// <summary>Mask edge feathering Gaussian blur radius in pixels.</summary>
    public static T2IRegisteredParam<int> MaskBlurParam;

    /// <summary>Replacement mode for the background (transparent, solid color, or mask).</summary>
    public static T2IRegisteredParam<string> BackgroundColorParam;

    /// <summary>Custom background color hex string (e.g. #FFFFFF).</summary>
    public static T2IRegisteredParam<string> CustomColorParam;

    /// <summary>Whether to fill interior holes via morphological closing.</summary>
    public static T2IRegisteredParam<bool> FillHolesParam;

    /// <summary>Whether to invert the mask (keep background, remove character).</summary>
    public static T2IRegisteredParam<bool> InvertMaskParam;

    /// <summary>Whether to save an intermediate copy of the unedited generated image before background removal.</summary>
    public static T2IRegisteredParam<bool> SaveOriginalParam;

    /// <summary>Optional input image to process directly through the interactive UI tool.</summary>
    public static T2IRegisteredParam<Image> InputImageParam;

    /// <summary>Called during extension preparation to register web assets and custom ComfyUI nodes.</summary>
    public override void OnPreInit()
    {
        ExtFolder = FilePath;
        ScriptFiles.Add("Assets/background_remover.js");
        StyleSheetFiles.Add("Assets/background_remover.css");
        ComfyUISelfStartBackend.CustomNodePaths.Add(Path.GetFullPath($"{FilePath}/ComfyNodes"));

        BackgroundRemoverGroup = new(
            "Character Background Removal",
            Toggles: true,
            Open: false,
            OrderPriority: 82,
            Description: "Isolates character and person subjects from generated images using YOLO segmentation or dedicated matting networks, cleanly eliminating backgrounds."
        );

        EngineParam = T2IParamTypes.Register<string>(new(
            "[Background Remover] Engine",
            "Segmentation engine used to identify and isolate character subjects.",
            "yolov8_person",
            GetValues: _ => [
                "yolov8_person///YOLOv8 Person Detection (COCO Class 0)",
                "yolo_refined///YOLO + Edge Matting (Soft Transitions)",
                "isnet_anime///ISNet Anime (Specialized for 2D/Anime Characters)",
                "u2net_human///U2Net Human (Specialized for Photographs)",
                "birefnet///BiRefNet (High-Precision Saliency)",
                "auto///Auto-Adaptive (YOLO with Anime/Human Fallback)"
            ],
            Group: BackgroundRemoverGroup,
            OrderPriority: 1
        ));

        YoloModelParam = T2IParamTypes.Register<string>(new(
            "[Background Remover] YOLO Model",
            "Ultralytics YOLO segmentation model checkpoint.",
            "yolov8m-seg.pt",
            GetValues: _ => [
                "yolov8m-seg.pt///YOLOv8 Medium (Recommended, balanced quality)",
                "yolov8s-seg.pt///YOLOv8 Small (Fast, lightweight)",
                "yolov8n-seg.pt///YOLOv8 Nano (Ultra-fast, lowest VRAM)",
                "yolov8l-seg.pt///YOLOv8 Large (High accuracy)",
                "yolov8x-seg.pt///YOLOv8 Extra Large (Maximum precision)",
                "yolo11n-seg.pt///YOLO11 Nano",
                "yolo11m-seg.pt///YOLO11 Medium",
                "yolo11x-seg.pt///YOLO11 Extra Large"
            ],
            Group: BackgroundRemoverGroup,
            OrderPriority: 2
        ));

        ConfidenceParam = T2IParamTypes.Register<double>(new(
            "[Background Remover] Confidence Threshold",
            "Minimum detection confidence score required to recognize a character.",
            "0.30",
            Min: 0.01,
            Max: 1.0,
            Step: 0.01,
            ViewMin: 0.05,
            ViewMax: 0.95,
            ViewType: ParamViewType.SLIDER,
            Group: BackgroundRemoverGroup,
            OrderPriority: 3
        ));

        PersonSelectionParam = T2IParamTypes.Register<string>(new(
            "[Background Remover] Subject Selection",
            "Determines which character subjects to preserve when multiple people are detected.",
            "all",
            GetValues: _ => [
                "all///Keep All Detected Characters",
                "largest///Keep Largest Character Only (Ignore background figures)",
                "primary///Keep Center-Most Character",
                "highest_conf///Keep Highest Confidence Character"
            ],
            Group: BackgroundRemoverGroup,
            OrderPriority: 4
        ));

        MaskPaddingParam = T2IParamTypes.Register<int>(new(
            "[Background Remover] Mask Edge Padding",
            "Expands (positive) or contracts (negative) mask edge boundaries in pixels.",
            "0",
            Min: -50,
            Max: 50,
            Step: 1,
            ViewMin: -30,
            ViewMax: 30,
            ViewType: ParamViewType.SLIDER,
            Group: BackgroundRemoverGroup,
            OrderPriority: 5
        ));

        MaskBlurParam = T2IParamTypes.Register<int>(new(
            "[Background Remover] Mask Feathering Blur",
            "Gaussian feathering blur radius in pixels to soften transitions for realistic hair blending.",
            "2",
            Min: 0,
            Max: 30,
            Step: 1,
            ViewMin: 0,
            ViewMax: 20,
            ViewType: ParamViewType.SLIDER,
            Group: BackgroundRemoverGroup,
            OrderPriority: 6
        ));

        BackgroundColorParam = T2IParamTypes.Register<string>(new(
            "[Background Remover] Output Background",
            "Target background style for the output image.",
            "transparent",
            GetValues: _ => [
                "transparent///Transparent (Alpha PNG)",
                "white///Solid White (#FFFFFF)",
                "black///Solid Black (#000000)",
                "greenscreen///Green Screen (#00FF00)",
                "custom///Custom Hex Color",
                "mask_only///Grayscale Alpha Mask Only"
            ],
            Group: BackgroundRemoverGroup,
            OrderPriority: 7
        ));

        CustomColorParam = T2IParamTypes.Register<string>(new(
            "[Background Remover] Custom Background Color",
            "Hex color code used when Custom Hex Color is selected (e.g. #FFFFFF or #1E1E2E).",
            "#FFFFFF",
            Group: BackgroundRemoverGroup,
            OrderPriority: 8
        ));

        FillHolesParam = T2IParamTypes.Register<bool>(new(
            "[Background Remover] Fill Interior Holes",
            "Applies morphological closing to prevent accidental transparent gaps inside clothing, body, or hair.",
            "true",
            Group: BackgroundRemoverGroup,
            OrderPriority: 9
        ));

        InvertMaskParam = T2IParamTypes.Register<bool>(new(
            "[Background Remover] Invert Mask",
            "Inverts the mask to eliminate character subjects and preserve the background.",
            "false",
            Group: BackgroundRemoverGroup,
            OrderPriority: 10
        ));

        SaveOriginalParam = T2IParamTypes.Register<bool>(new(
            "[Background Remover] Save Original Before Removal",
            "Saves an intermediate copy of the unedited generated image before background removal.",
            "false",
            Group: BackgroundRemoverGroup,
            OrderPriority: 11
        ));

        InputImageParam = T2IParamTypes.Register<Image>(new(
            "[Background Remover] Input Image",
            "Existing image to isolate characters and remove background from using the interactive tool.",
            Default: null,
            ImageShouldResize: false,
            Group: BackgroundRemoverGroup,
            OrderPriority: 12
        ));

        WorkflowGenerator.AddStep(g =>
        {
            if (g.UserInput.TryGet(EngineParam, out string engine))
            {
                if (g.CurrentMedia is null)
                {
                    return;
                }
                g.CurrentMedia = g.CurrentMedia.AsRawImage(g.CurrentVae);
                if (g.UserInput.Get(SaveOriginalParam, false))
                {
                    g.SaveOptionalIntermediate();
                }
                string yoloModel = g.UserInput.Get(YoloModelParam, "yolov8m-seg.pt");
                double confidence = g.UserInput.Get(ConfidenceParam, 0.30);
                string personSelection = g.UserInput.Get(PersonSelectionParam, "all");
                int padding = g.UserInput.Get(MaskPaddingParam, 0);
                int blur = g.UserInput.Get(MaskBlurParam, 2);
                string bgColor = g.UserInput.Get(BackgroundColorParam, "transparent");
                string customColor = g.UserInput.Get(CustomColorParam, "#FFFFFF");
                bool invert = g.UserInput.Get(InvertMaskParam, false);
                bool fillHoles = g.UserInput.Get(FillHolesParam, true);

                string removedNode = g.CreateNode("CharacterBackgroundRemover", new JObject()
                {
                    ["images"] = g.CurrentMedia.Path,
                    ["engine"] = engine,
                    ["yolo_model"] = yoloModel,
                    ["confidence"] = confidence,
                    ["person_selection"] = personSelection,
                    ["mask_padding"] = padding,
                    ["mask_blur"] = blur,
                    ["background_color"] = bgColor,
                    ["custom_color"] = customColor,
                    ["invert_mask"] = invert,
                    ["fill_holes"] = fillHoles
                });
                g.CurrentMedia = g.CurrentMedia.WithPath([removedNode, 0], mayHaveAlpha: bgColor == "transparent");
            }
        }, 52);
    }

    /// <summary>Called when extension initializes to register API endpoints.</summary>
    public override void OnInit()
    {
        BackgroundRemoverAPI.Register();
        Logs.Init("SwarmUI-BackgroundRemover Extension loaded.");
    }
}
