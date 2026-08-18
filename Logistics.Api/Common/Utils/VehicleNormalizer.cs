using System.Text.RegularExpressions;

namespace Logistics.Api.Common.Utils;

public static class VehicleNormalizer
{
    public static string NormalizePlate(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;

        // 1. Hapus SEMUA spasi, strip, underscore, dan ubah ke huruf besar
        var cleaned = Regex.Replace(rawPlate.ToUpperInvariant(), @"[^A-Z0-9]", "");

        // 2. Gunakan Regex capture groups untuk memisahkan sesuai aturan Plat Indonesia:
        // (1-2 Huruf Area)(1-4 Angka)(0-3 Huruf Belakang)
        var match = Regex.Match(cleaned, @"^([A-Z]{1,2})([0-9]{1,4})([A-Z]{0,3})$");

        if (match.Success)
        {
            var parts = new List<string> { match.Groups[1].Value, match.Groups[2].Value };
            if (!string.IsNullOrEmpty(match.Groups[3].Value))
            {
                parts.Add(match.Groups[3].Value);
            }
            // 3. Gabungkan dengan pemisah baku strip (-)
            return string.Join("-", parts);
        }

        // Jika tidak sesuai pola plat standar, kembalikan versi cleaned-up saja
        return cleaned;
    }

    public static bool IsValidStandardPlate(string normalizedPlate)
    {
        // Mengecek apakah string cocok dengan format baku A-1234-BCD
        return Regex.IsMatch(normalizedPlate, @"^[A-Z]{1,2}-[0-9]{1,4}(-[A-Z]{1,3})?$");
    }
}