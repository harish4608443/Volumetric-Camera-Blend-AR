using UnityEngine;
using System.Collections.Generic;

public static class TextureTextDrawer
{
    private static readonly int[,] font5x7 = new int[,]
    {
        // Simple 5x7 font bitmap for basic characters (A-Z, 0-9, space, %)
        // Each row is 5 pixels, 7 rows per character
    };
    
    public static void DrawText(Texture2D texture, string text, int x, int y, Color color, int scale = 2, bool reverseText = false, bool flipHorizontal = false)
    {
        text = text.ToUpper();
        
        if (reverseText)
        {
            // Draw text right-to-left for mirrored displays
            int cursorX = x;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                DrawChar(texture, text[i], cursorX, y, color, scale, flipHorizontal);
                cursorX += 6 * scale;
            }
        }
        else
        {
            // Normal left-to-right
            int cursorX = x;
            foreach (char c in text)
            {
                DrawChar(texture, c, cursorX, y, color, scale, flipHorizontal);
                cursorX += 6 * scale;
            }
        }
    }
    
    private static void DrawChar(Texture2D texture, char c, int x, int y, Color color, int scale, bool flipHorizontal = false)
    {
        // Simple pixel font - draw common characters
        int[,] charPixels = GetCharPixels(c);
        if (charPixels == null) return;
        
        for (int py = 0; py < 7; py++)
        {
            for (int px = 0; px < 5; px++)
            {
                // Flip px horizontally if needed (mirror the character)
                int actualPx = flipHorizontal ? (4 - px) : px;
                
                if (charPixels[py, actualPx] == 1)
                {
                    // Draw scaled pixel with alpha blending support
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int texX = x + px * scale + sx;
                            int texY = y + py * scale + sy;
                            if (texX >= 0 && texX < texture.width && texY >= 0 && texY < texture.height)
                            {
                                if (color.a < 1f)
                                {
                                    // Alpha blend for fade effect
                                    Color existing = texture.GetPixel(texX, texY);
                                    Color blended = Color.Lerp(existing, color, color.a);
                                    texture.SetPixel(texX, texY, blended);
                                }
                                else
                                {
                                    texture.SetPixel(texX, texY, color);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    private static int[,] GetCharPixels(char c)
    {
        // Simple 5x7 pixel font for essential characters
        switch (c)
        {
            case 'A': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}};
            case 'B': return new int[,] {
                {1,1,1,1,0}, {1,0,0,0,1}, {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,0}};
            case 'C': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,1}, {0,1,1,1,0}};
            case 'D': return new int[,] {
                {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,0}};
            case 'E': return new int[,] {
                {1,1,1,1,1}, {1,0,0,0,0}, {1,0,0,0,0}, {1,1,1,1,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,1,1,1,1}};
            case 'K': return new int[,] {
                {1,0,0,0,1}, {1,0,0,1,0}, {1,0,1,0,0}, {1,1,0,0,0}, {1,0,1,0,0}, {1,0,0,1,0}, {1,0,0,0,1}};
            case 'L': return new int[,] {
                {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,1,1,1,1}};
            case 'N': return new int[,] {
                {1,0,0,0,1}, {1,1,0,0,1}, {1,0,1,0,1}, {1,0,0,1,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}};
            case 'O': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}};
            case 'P': return new int[,] {
                {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}};
            case 'R': return new int[,] {
                {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,0}, {1,0,1,0,0}, {1,0,0,1,0}, {1,0,0,0,1}};
            case 'S': return new int[,] {
                {0,1,1,1,1}, {1,0,0,0,0}, {1,0,0,0,0}, {0,1,1,1,0}, {0,0,0,0,1}, {0,0,0,0,1}, {1,1,1,1,0}};
            case 'T': return new int[,] {
                {1,1,1,1,1}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}};
            case '0': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,1,1}, {1,0,1,0,1}, {1,1,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}};
            case '1': return new int[,] {
                {0,0,1,0,0}, {0,1,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,0,1,0,0}, {0,1,1,1,0}};
            case '2': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {0,0,0,0,1}, {0,0,0,1,0}, {0,0,1,0,0}, {0,1,0,0,0}, {1,1,1,1,1}};
            case '3': return new int[,] {
                {1,1,1,1,0}, {0,0,0,0,1}, {0,0,0,0,1}, {0,1,1,1,0}, {0,0,0,0,1}, {0,0,0,0,1}, {1,1,1,1,0}};
            case '4': return new int[,] {
                {0,0,0,1,0}, {0,0,1,1,0}, {0,1,0,1,0}, {1,0,0,1,0}, {1,1,1,1,1}, {0,0,0,1,0}, {0,0,0,1,0}};
            case '5': return new int[,] {
                {1,1,1,1,1}, {1,0,0,0,0}, {1,1,1,1,0}, {0,0,0,0,1}, {0,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}};
            case '6': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}};
            case '7': return new int[,] {
                {1,1,1,1,1}, {0,0,0,0,1}, {0,0,0,1,0}, {0,0,1,0,0}, {0,1,0,0,0}, {0,1,0,0,0}, {0,1,0,0,0}};
            case '8': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}};
            case '9': return new int[,] {
                {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,1}, {0,0,0,0,1}, {0,0,0,0,1}, {0,1,1,1,0}};
            case '%': return new int[,] {
                {1,1,0,0,1}, {1,1,0,1,0}, {0,0,1,0,0}, {0,1,0,0,0}, {1,0,0,1,1}, {1,0,0,1,1}, {0,0,0,0,0}};
            case ' ': return new int[,] {
                {0,0,0,0,0}, {0,0,0,0,0}, {0,0,0,0,0}, {0,0,0,0,0}, {0,0,0,0,0}, {0,0,0,0,0}, {0,0,0,0,0}};
            default: return null;
        }
    }
}
