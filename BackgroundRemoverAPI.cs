using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace SwarmUI.BackgroundRemover;

/// <summary>Permission definitions for Character Background Remover.</summary>
public static class BackgroundRemoverPermissions
{
    /// <summary>Permission group for BackgroundRemover extension.</summary>
    public static readonly PermInfoGroup BackgroundRemoverPermGroup = new("BackgroundRemover", "Permissions related to SwarmUI Background Remover extension.");

    /// <summary>Permission to run character background removal on images.</summary>
    public static readonly PermInfo PermRemoveBackground = Permissions.Register(new(
        "background_remover_process",
        "Remove Character Background",
        "Allows running character segmentation and background removal on images.",
        PermissionDefault.USER,
        BackgroundRemoverPermGroup));
}

/// <summary>Web API controller for Background Remover operations.</summary>
[API.APIClass("API routes for SwarmUI Background Remover extension")]
public static class BackgroundRemoverAPI
{
    /// <summary>Registers API routes for this extension.</summary>
    public static void Register()
    {
        API.RegisterAPICall(RemoveCharacterBackground, false, BackgroundRemoverPermissions.PermRemoveBackground);
    }

    /// <summary>Removes background from an input image leaving only character/person subjects.</summary>
    [API.APIDescription("Removes the background from an image leaving only character subjects using YOLO or specialized matting.", "{'success':true,'image':'base64...','image_type':'image/png'}")]
    public static async Task<JObject> RemoveCharacterBackground(
        Session session,
        [API.APIParameter("Base64 string representing the input image.")] string imageBase64,
        [API.APIParameter("Segmentation engine to use (yolov8_person, yolo_refined, isnet_anime, u2net_human, birefnet, auto).")] string engine = "yolov8_person",
        [API.APIParameter("Ultralytics YOLO model checkpoint name.")] string yoloModel = "yolov8m-seg.pt",
        [API.APIParameter("Minimum detection confidence threshold.")] double confidence = 0.30,
        [API.APIParameter("Subject selection strategy (all, largest, primary, highest_conf).")] string personSelection = "all",
        [API.APIParameter("Mask dilation/erosion in pixels.")] int maskPadding = 0,
        [API.APIParameter("Feathering blur radius in pixels.")] int maskBlur = 2,
        [API.APIParameter("Target background mode (transparent, white, black, greenscreen, custom, mask_only).")] string backgroundColor = "transparent",
        [API.APIParameter("Custom background color hex string.")] string customColor = "#FFFFFF",
        [API.APIParameter("Whether to invert the mask.")] bool invertMask = false,
        [API.APIParameter("Whether to fill interior holes.")] bool fillHoles = true)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "No image data provided."
            };
        }

        if (imageBase64.StartsWith("data:"))
        {
            int commaIndex = imageBase64.IndexOf(',');
            if (commaIndex >= 0)
            {
                imageBase64 = imageBase64[(commaIndex + 1)..];
            }
        }

        ComfyUIAPIAbstractBackend backend = ComfyUIBackendExtension.RunningComfyBackends.FirstOrDefault(b => b is ComfyUISelfStartBackend)
            ?? ComfyUIBackendExtension.RunningComfyBackends.FirstOrDefault();

        if (backend is null)
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "No active ComfyUI backend available to process the image."
            };
        }

        JObject workflow = new()
        {
            ["1"] = new JObject()
            {
                ["class_type"] = "SwarmLoadImageB64",
                ["inputs"] = new JObject()
                {
                    ["image_base64"] = imageBase64
                }
            },
            ["2"] = new JObject()
            {
                ["class_type"] = "CharacterBackgroundRemover",
                ["inputs"] = new JObject()
                {
                    ["images"] = new JArray() { "1", 0 },
                    ["engine"] = engine,
                    ["yolo_model"] = yoloModel,
                    ["confidence"] = confidence,
                    ["person_selection"] = personSelection,
                    ["mask_padding"] = maskPadding,
                    ["mask_blur"] = maskBlur,
                    ["background_color"] = backgroundColor,
                    ["custom_color"] = customColor,
                    ["invert_mask"] = invertMask,
                    ["fill_holes"] = fillHoles
                }
            },
            ["9"] = new JObject()
            {
                ["class_type"] = "SwarmSaveImageWS",
                ["inputs"] = new JObject()
                {
                    ["images"] = new JArray() { "2", 0 }
                }
            }
        };

        MediaFile resultFile = null;
        T2IParamInput customInput = new(session);

        using Session.GenClaim claim = session?.Claim(liveGens: 1);
        await backend.AwaitJobLive(workflow.ToString(), "0", outputObj =>
        {
            if (outputObj is T2IEngine.ImageOutput imgOut && imgOut.File is not null)
            {
                resultFile = imgOut.File;
            }
        }, customInput, Program.GlobalProgramCancel);

        if (resultFile is null)
        {
            return new JObject()
            {
                ["success"] = false,
                ["error"] = "The workflow completed, but no image output was produced."
            };
        }

        return new JObject()
        {
            ["success"] = true,
            ["image"] = resultFile.AsBase64,
            ["image_type"] = resultFile.Type.MimeType
        };
    }
}
