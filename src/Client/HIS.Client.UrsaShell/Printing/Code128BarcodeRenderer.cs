using System;
using System.Collections.Generic;
using System.Text;

namespace HIS.Client.UrsaShell.Printing;

/// <summary>
/// 纯原生 C# Code 128-B 条码编码器（零外部依赖，可直接渲染为 Avalonia 矢量条形或 SVG 打印条码）
/// </summary>
public static class Code128BarcodeRenderer
{
    private static readonly string[] Patterns =
    {
        "11011001100","11001101100","11001100110","10010011000","10010001100","10001001100","10011001000","10011000100","10001100100","11001001000",
        "11001000100","11000100100","10110011100","10011011100","10011001110","10111001100","10011101100","10011100110","11001110010","11001011100",
        "11001001110","11011100100","11001110100","11101101110","11101001100","11100101100","11100100110","11101100100","11100110100","11100110010",
        "11011011000","11011000110","11000110110","10100011000","10001011000","10001000110","10110001000","10001101000","10001100010","11010001000",
        "11000101000","11000100010","10110111000","10110001110","10001101110","10111011000","10111000110","10001110110","11101110110","11010001110",
        "11000101110","11011101000","11011100010","11011101110","11101011000","11101000110","11100010110","11101101000","11101100010","11100011010",
        "11101111010","11001000010","11110001010","10100110000","10100001100","10010110000","10010000110","10000101100","10000100110","10110010000",
        "10110000100","10011010000","10011000010","10000110100","10000110010","11000010010","11001010000","11110111010","11000010100","10001111010",
        "10100111100","10010111100","10010011110","10111100100","10011110100","10011110010","11110100100","11110010100","11110010010","11011011110",
        "11011110110","11110110110","10101111000","10100011110","10001011110","10111101000","10111100010","11110101000","11110100010","10111011110",
        "10111101110","11101011110","11110101110","11010000100","11010010000","11010011100","1100011101011"
    };

    /// <summary>
    /// 将任意 ASCII 文本编码为 Code 128-B 二进制条空位串（1=黑条，0=白空）
    /// </summary>
    public static string EncodeToBits(string? rawText)
    {
        string text = SanitizeAscii(rawText);
        if (string.IsNullOrEmpty(text)) text = "000000";

        var codes = new List<int> { 104 }; // Start Code B
        int checksum = 104;
        for (int i = 0; i < text.Length; i++)
        {
            int code = Math.Clamp(text[i] - 32, 0, 94);
            codes.Add(code);
            checksum += code * (i + 1);
        }
        codes.Add(checksum % 103);
        codes.Add(106); // Stop

        var sb = new StringBuilder();
        foreach (int c in codes)
            sb.Append(Patterns[c]);
        return sb.ToString();
    }

    public static string RenderSvg(string? rawText, double widthMm, double heightMm, bool showText)
    {
        string text = string.IsNullOrWhiteSpace(rawText) ? "000000" : rawText.Trim();
        string bits = EncodeToBits(text);
        double barAreaH = showText ? Math.Max(4, heightMm - 3.2) : heightMm;
        double moduleW = widthMm / Math.Max(1, bits.Length);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{widthMm:0.##}mm\" height=\"{heightMm:0.##}mm\" viewBox=\"0 0 {widthMm:0.##} {heightMm:0.##}\">");
        int i = 0;
        while (i < bits.Length)
        {
            if (bits[i] == '1')
            {
                int start = i;
                while (i < bits.Length && bits[i] == '1') i++;
                double x = start * moduleW;
                double w = (i - start) * moduleW;
                sb.Append($"<rect x=\"{x:0.###}\" y=\"0\" width=\"{w:0.###}\" height=\"{barAreaH:0.##}\" fill=\"#000\"/>");
            }
            else
            {
                i++;
            }
        }
        if (showText)
        {
            string safe = System.Net.WebUtility.HtmlEncode(text);
            sb.Append($"<text x=\"{widthMm / 2:0.##}\" y=\"{heightMm - 0.5:0.##}\" text-anchor=\"middle\" font-family=\"Consolas,monospace\" font-size=\"2.8\" fill=\"#000\">{safe}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string SanitizeAscii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder();
        foreach (char ch in input.Trim())
        {
            if (ch >= 32 && ch <= 126) sb.Append(ch);
        }
        return sb.ToString();
    }
}
