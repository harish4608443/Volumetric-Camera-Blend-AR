using System;
using UnityEngine;

public static class TextureTools
{
    public static Texture2D ResizeAndCropToCenter(Texture texture, ref Texture2D result, int targetWidth, int targetHeight)
    {
        if (texture == null || result == null)
        {
            Debug.LogError("Input texture or result texture is null.");
            return null;
        }

        float widthRatio = targetWidth / (float)texture.width;
        float heightRatio = targetHeight / (float)texture.height;

        float scaleRatio = Mathf.Max(widthRatio, heightRatio);

        int scaledWidth = Mathf.CeilToInt(texture.width * scaleRatio);
        int scaledHeight = Mathf.CeilToInt(texture.height * scaleRatio);

        // Use sRGB=true to match camera color space
        RenderTexture renderTexture = RenderTexture.GetTemporary(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previousRT = RenderTexture.active;

        Graphics.Blit(texture, renderTexture);
        RenderTexture.active = renderTexture;

        int xOffset = (scaledWidth - targetWidth) / 2;
        int yOffset = (scaledHeight - targetHeight) / 2;

        result.ReadPixels(new Rect(xOffset, yOffset, targetWidth, targetHeight), 0, 0);
        result.Apply();

        RenderTexture.active = previousRT;
        RenderTexture.ReleaseTemporary(renderTexture);

        return result;
    }

    public static void DrawRectOutline(Texture2D texture, Rect rect, Color color, int width = 1, bool rectIsNormalized = true, bool revertY = false)
    {
        if (rectIsNormalized)
        {
            rect.x *= texture.width;
            rect.y *= texture.height;
            rect.width *= texture.width;
            rect.height *= texture.height;
        }

        if (revertY)
            rect.y = rect.y * -1 + texture.height - rect.height;

        if (rect.width <= 0 || rect.height <= 0)
            return;

        DrawRect(texture, rect.x, rect.y, rect.width + width, width, color);
        DrawRect(texture, rect.x, rect.y + rect.height, rect.width + width, width, color);

        DrawRect(texture, rect.x, rect.y, width, rect.height + width, color);
        DrawRect(texture, rect.x + rect.width, rect.y, width, rect.height + width, color);
        texture.Apply();
    }

    public static void DrawFilledRect(Texture2D texture, Rect rect, Color color, bool rectIsNormalized = true, bool revertY = false)
    {
        if (rectIsNormalized)
        {
            rect.x *= texture.width;
            rect.y *= texture.height;
            rect.width *= texture.width;
            rect.height *= texture.height;
        }

        if (revertY)
            rect.y = rect.y * -1 + texture.height - rect.height;

        if (rect.width <= 0 || rect.height <= 0)
            return;

        DrawRect(texture, rect.x, rect.y, rect.width, rect.height, color);
        texture.Apply();
    }

    static private void DrawRect(Texture2D texture, float x, float y, float width, float height, Color color)
    {
        if (x > texture.width || y > texture.height)
            return;

        if (x < 0)
        {
            width += x;
            x = 0;
        }
        if (y < 0)
        {
            height += y;
            y = 0;
        }

        width = x + width > texture.width ? texture.width - x : width;
        height = y + height > texture.height ? texture.height - y : height;

        x = (int)x;
        y = (int)y;
        width = (int)width;
        height = (int)height;

        if (width <= 0 || height <= 0)
            return;

        // Alpha blending support for fade effect
        if (color.a < 1f)
        {
            Color[] existingPixels = texture.GetPixels((int)x, (int)y, (int)width, (int)height);
            Color[] blendedPixels = new Color[existingPixels.Length];
            
            for (int i = 0; i < existingPixels.Length; i++)
            {
                // Alpha blend: result = src * alpha + dst * (1 - alpha)
                blendedPixels[i] = Color.Lerp(existingPixels[i], color, color.a);
            }
            
            texture.SetPixels((int)x, (int)y, (int)width, (int)height, blendedPixels);
        }
        else
        {
            // Opaque - use faster Color32 path
            int pixelsCount = (int)width * (int)height;
            Color32[] colors = new Color32[pixelsCount];
            for (int i = 0; i < pixelsCount; i++)
                colors[i] = color;

            texture.SetPixels32((int)x, (int)y, (int)width, (int)height, colors);
        }
    }
}
